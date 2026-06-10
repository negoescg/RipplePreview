import { css, html, nothing, property, query, state, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type {
	UmbBlockEditorCustomViewConfiguration,
	UmbBlockEditorCustomViewElement,
} from '@umbraco-cms/backoffice/block-custom-view';
import type { UmbBlockDataType, UmbBlockLayoutBaseModel } from '@umbraco-cms/backoffice/block';
import { UMB_BLOCK_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/block';
import type { UmbBlockTypeBaseModel } from '@umbraco-cms/backoffice/block-type';
import { UMB_CONTENT_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/content';
import { observeMultiple } from '@umbraco-cms/backoffice/observable-api';
import { postRender, type RippleRenderRequest, type RippleRenderResponse } from '../api.js';
import { getRippleSettings } from '../settings.js';
import { enqueue } from '../queue.js';
import { isStructureMode, onStructureModeChange, toggleStructureMode } from '../structure-mode.js';
import { togglePreviewMode } from '../preview-mode.js';
import { RIPPLE_GHOST_AREAS_CONTEXT, type RippleGhostAreasContext } from '../ghost-context.js';
import { RippleZoomOverlayElement } from './ripple-zoom-overlay.element.js';

const jsonChanged = (value: unknown, old: unknown) => JSON.stringify(value) !== JSON.stringify(old);

/**
 * Base custom view: renders the block's real frontend markup inside a same-origin iframe
 * locked to the configured design width, scaled to fit the entry. Double-buffered so
 * re-renders never flash, with a hover toolbar (zoom / refresh / interact).
 */
export abstract class RippleBaseViewElement extends UmbLitElement implements UmbBlockEditorCustomViewElement {
	@property({ attribute: false, hasChanged: jsonChanged })
	content?: UmbBlockDataType;

	@property({ attribute: false, hasChanged: jsonChanged })
	settings?: UmbBlockDataType;

	@property({ attribute: false })
	contentKey?: string;

	@property({ attribute: false })
	label?: string;

	@property({ attribute: false })
	icon?: string;

	@property({ attribute: false })
	index?: number;

	@property({ attribute: false })
	config?: UmbBlockEditorCustomViewConfiguration;

	@property({ attribute: false })
	blockType?: UmbBlockTypeBaseModel;

	@property({ attribute: false, hasChanged: jsonChanged })
	layout?: UmbBlockLayoutBaseModel;

	@property({ attribute: false })
	contentInvalid?: boolean;

	@property({ attribute: false })
	settingsInvalid?: boolean;

	@property({ attribute: false })
	unpublished?: boolean;

	@property({ attribute: false })
	readonly?: boolean;

	@state() protected _docA = '';
	@state() protected _docB = '';
	@state() protected _activeBuffer: 'a' | 'b' = 'a';
	@state() protected _heights: Record<'a' | 'b', number> = { a: 0, b: 0 };
	@state() protected _hostWidth = 0;
	@state() protected _hostHeight = 0;
	@state() protected _fraction = 1;
	@state() protected _loading = true;
	@state() protected _error = '';
	@state() protected _interactive = false;
	@state() protected _hasRendered = false;
	@state() protected _structure = isStructureMode();

	@query('#frame-a') private _frameA?: HTMLIFrameElement;
	@query('#frame-b') private _frameB?: HTMLIFrameElement;

	protected _documentKey: string | null = null;
	protected _documentTypeKey = '';
	protected _culture: string | null = null;
	protected _propertyAlias = '';

	#docTypeFromBlockWorkspace = false;
	#requestId = 0;
	#debounceTimer?: number;
	#pendingBuffer: 'a' | 'b' | null = null;
	#resizeObserver?: ResizeObserver;
	#currentDoc = '';
	#unsubscribeStructure?: () => void;
	#ghostContext?: RippleGhostAreasContext;
	#probeSeq = 0;
	#pendingClickProbe: number | null = null;
	#clickFallbackTimer?: number;
	#lastHoverProbe = 0;

	/** The render endpoint segment: 'grid', 'list', 'rte' or 'single'. */
	protected abstract readonly editorKind: import('../api.js').RippleEditorKind;

	/** Builds the render request from the latest observed state. Null when not ready. */
	protected abstract buildRequest(): RippleRenderRequest | null;

	/** Opens this block's edit workspace. */
	protected abstract openEdit(): void;

	constructor() {
		super();

		// When an ancestor block renders its children inside its own preview, this view
		// becomes a compact editing bar instead of fetching a duplicate preview.
		this.consumeContext(RIPPLE_GHOST_AREAS_CONTEXT, (context) => {
			this.#ghostContext = context;
			this.requestUpdate();
		});

		this.consumeContext(UMB_CONTENT_WORKSPACE_CONTEXT, (workspace) => {
			if (!workspace) return;
			this.observe(
				observeMultiple([workspace.unique, workspace.structure.contentTypeUniques]),
				([unique, contentTypeUniques]) => {
					this._documentKey = (unique as string | undefined) ?? null;
					if (!this.#docTypeFromBlockWorkspace) {
						this._documentTypeKey = contentTypeUniques?.[0] ?? this._documentTypeKey;
					}
					this.requestRender();
				},
				'rippleContentWorkspace',
			);
		});

		// Inside a block workspace modal (block-editor-in-block), the property sits on the element
		// type — prefer it for property/config resolution on the server.
		this.consumeContext(UMB_BLOCK_WORKSPACE_CONTEXT, (workspace) => {
			if (!workspace) return;
			this.observe(
				workspace.content.structure.contentTypeUniques,
				(contentTypeUniques) => {
					const key = contentTypeUniques?.[0];
					if (key) {
						this._documentTypeKey = key;
						this.#docTypeFromBlockWorkspace = true;
						this.requestRender();
					}
				},
				'rippleBlockWorkspace',
			);
		});
	}

	override connectedCallback() {
		super.connectedCallback();
		this.#unsubscribeStructure = onStructureModeChange((value) => {
			this._structure = value;
		});
		window.addEventListener('message', this.#onMessage);
		window.addEventListener('ripple:open-child', this.#onOpenChild as EventListener);
		this.#resizeObserver = new ResizeObserver((entries) => {
			const rect = entries[0]?.contentRect;
			if (!rect) return;
			if (rect.width > 0 && Math.abs(rect.width - this._hostWidth) > 0.5) {
				this._hostWidth = rect.width;
			}
			if (rect.height > 0 && Math.abs(rect.height - this._hostHeight) > 0.5) {
				this._hostHeight = rect.height;
			}
		});
		this.#resizeObserver.observe(this);
	}

	override disconnectedCallback() {
		super.disconnectedCallback();
		this.#unsubscribeStructure?.();
		window.removeEventListener('message', this.#onMessage);
		window.removeEventListener('ripple:open-child', this.#onOpenChild as EventListener);
		window.clearTimeout(this.#clickFallbackTimer);
		this.#resizeObserver?.disconnect();
		window.clearTimeout(this.#debounceTimer);
	}

	protected override updated(changed: PropertyValues) {
		super.updated(changed);
		if (changed.has('content') || changed.has('settings') || changed.has('layout')) {
			this.requestRender();
		}
	}

	/** True when an ancestor's preview already shows this block. */
	protected get isGhost(): boolean {
		return this.#ghostContext?.enabled === true;
	}

	/** True when the preview document carries a child map for cursor hit-testing. */
	protected get supportsChildHitTesting(): boolean {
		return false;
	}

	/** Opens the editor of a child block shown inside this preview. */
	protected openChild(key: string) {
		window.dispatchEvent(new CustomEvent('ripple:open-child', { detail: { key } }));
	}

	#onOpenChild = (event: CustomEvent<{ key?: string }>) => {
		if (event.detail?.key && event.detail.key === this.contentKey) {
			this.openEdit();
		}
	};

	get #activeFrame(): HTMLIFrameElement | undefined {
		return this._activeBuffer === 'a' ? this._frameA : this._frameB;
	}

	#sendProbe(event: MouseEvent, hover: boolean, probeId?: number) {
		const frame = this.#activeFrame;
		const scale = this.scale;
		if (!frame?.contentWindow || scale <= 0) return;
		frame.contentWindow.postMessage(
			{
				source: 'ripple-preview-host',
				type: 'hit-test',
				x: event.offsetX / scale,
				y: event.offsetY / scale,
				hover,
				probeId,
			},
			'*',
		);
	}

	protected requestRender() {
		if (this.isGhost) return;
		window.clearTimeout(this.#debounceTimer);
		this.#debounceTimer = window.setTimeout(() => this.#render(), 350);
	}

	protected forceRender() {
		this.#currentDoc = '';
		this.requestRender();
	}

	async #render() {
		if (this.isGhost) return;
		const request = this.buildRequest();
		if (!request) return;

		const requestId = ++this.#requestId;
		this._loading = true;

		try {
			const { data, error } = await enqueue(() => postRender(this.editorKind, request));
			if (requestId !== this.#requestId) return;

			if (!data || error) {
				this._error = 'Preview could not be rendered.';
				this._loading = false;
				return;
			}

			this.#applyResponse(data);
		} catch (err) {
			if (requestId !== this.#requestId) return;
			console.error('[RipplePreview] render failed', err);
			this._error = 'Preview could not be rendered.';
			this._loading = false;
		}
	}

	#applyResponse(data: RippleRenderResponse) {
		this._error = '';
		this._fraction = data.widthFraction > 0 ? data.widthFraction : 1;

		if (data.html === this.#currentDoc) {
			this._loading = false;
			return;
		}
		this.#currentDoc = data.html;

		// Double buffer: load into the inactive iframe, swap once it reports its size.
		const target: 'a' | 'b' = this._activeBuffer === 'a' && this._hasRendered ? 'b' : 'a';
		this.#pendingBuffer = target;
		if (target === 'a') this._docA = data.html;
		else this._docB = data.html;
	}

	#onMessage = (event: MessageEvent) => {
		const payload = event.data;
		if (!payload || payload.source !== 'ripple-preview') return;

		if (payload.type === 'child-hit') {
			if (event.source !== this.#activeFrame?.contentWindow) return;
			if (!payload.hover && payload.probeId === this.#pendingClickProbe) {
				this.#pendingClickProbe = null;
				window.clearTimeout(this.#clickFallbackTimer);
				if (payload.key) this.openChild(payload.key as string);
				else this.#openOwnEditor();
			}
			return;
		}

		if (payload.type !== 'size') return;

		const buffer: 'a' | 'b' | null =
			event.source === this._frameA?.contentWindow ? 'a'
			: event.source === this._frameB?.contentWindow ? 'b'
			: null;
		if (!buffer) return;

		const height = Math.max(0, Number(payload.height) || 0);
		if (this._heights[buffer] !== height) {
			this._heights = { ...this._heights, [buffer]: height };
		}

		if (this.#pendingBuffer === buffer) {
			this.#pendingBuffer = null;
			this._activeBuffer = buffer;
			this._loading = false;
			this._hasRendered = true;
		}
	};

	protected get designWidth(): number {
		return getRippleSettings().designWidth || 1440;
	}

	protected get naturalWidth(): number {
		return this.designWidth * this._fraction;
	}

	protected get scale(): number {
		if (this._hostWidth <= 0 || this.naturalWidth <= 0) return 1;
		return Math.min(1, this._hostWidth / this.naturalWidth);
	}

	#openOwnEditor() {
		if (this.readonly || !this.config?.showContentEdit) return;
		this.openEdit();
	}

	#onHitClick(event: MouseEvent) {
		event.preventDefault();
		event.stopPropagation();
		this.#openOwnEditor();
	}

	#onStageClick(event: MouseEvent) {
		event.preventDefault();
		event.stopPropagation();

		if (!this.supportsChildHitTesting) {
			this.#openOwnEditor();
			return;
		}

		// Ask the preview document what sits under the cursor: a child opens its own
		// editor, anything else opens this block. Falls back to this block if the
		// document doesn't answer in time.
		const probeId = ++this.#probeSeq;
		this.#pendingClickProbe = probeId;
		window.clearTimeout(this.#clickFallbackTimer);
		this.#clickFallbackTimer = window.setTimeout(() => {
			if (this.#pendingClickProbe === probeId) {
				this.#pendingClickProbe = null;
				this.#openOwnEditor();
			}
		}, 200);
		this.#sendProbe(event, false, probeId);
	}

	#onStageMove(event: MouseEvent) {
		if (!this.supportsChildHitTesting) return;
		const now = performance.now();
		if (now - this.#lastHoverProbe < 80) return;
		this.#lastHoverProbe = now;
		this.#sendProbe(event, true);
	}

	#onStageLeave() {
		if (!this.supportsChildHitTesting) return;
		this.#activeFrame?.contentWindow?.postMessage({ source: 'ripple-preview-host', type: 'hover-end' }, '*');
	}

	#onZoom(event: Event) {
		event.stopPropagation();
		if (!this.#currentDoc) return;
		const overlay = new RippleZoomOverlayElement();
		overlay.doc = this.#currentDoc;
		overlay.designWidth = this.designWidth;
		overlay.naturalWidth = this.naturalWidth;
		overlay.contentHeight = this._heights[this._activeBuffer];
		overlay.headline = this.label ?? '';
		document.body.appendChild(overlay);
	}

	#onRefresh(event: Event) {
		event.stopPropagation();
		this.forceRender();
	}

	#onToggleInteractive(event: Event) {
		event.stopPropagation();
		this._interactive = !this._interactive;
	}

	#onToggleStructure(event: Event) {
		event.stopPropagation();
		toggleStructureMode();
	}

	/** Size badge for the chip, e.g. "6 × 2" for grid blocks. Overridden per editor. */
	protected get sizeBadge(): string | null {
		return null;
	}

	/** Backdrop mode: the preview stretches behind natively editable areas (grid parents). */
	protected get backdropMode(): boolean {
		return false;
	}

	/** True when this block sits inside another block's area (transparent stage). */
	protected get isNested(): boolean {
		return false;
	}

	/** True when the rendered preview is (near) invisible — spacers, script-driven blocks, etc. */
	protected get isEmptyPreview(): boolean {
		if (this.backdropMode) return false;
		return this._hasRendered && !this._loading && this._heights[this._activeBuffer] < 20;
	}

	#onPreviewOff(event: Event) {
		event.stopPropagation();
		togglePreviewMode();
	}

	protected renderStage() {
		const scale = this.scale;
		const empty = this.isEmptyPreview;
		const backdrop = this.backdropMode;
		const activeHeight = this._heights[this._activeBuffer];
		let stageHeight = activeHeight > 0 ? Math.ceil(activeHeight * scale) : 96;
		if (empty) stageHeight = Math.max(stageHeight, 64);

		const classes = [
			'stage',
			this._structure ? 'structure' : '',
			empty ? 'empty' : '',
			backdrop ? 'backdrop' : '',
			this.isNested ? 'transparent' : '',
		].join(' ');

		return html`
			<div class=${classes} style=${backdrop ? '' : `height:${stageHeight}px`}>
				${this.#renderFrame('a')}
				${this.#renderFrame('b')}
				${this._interactive
					? nothing
					: html`<div
							class="hit"
							title=${this.config?.showContentEdit ? 'Click to edit' : ''}
							@click=${this.#onStageClick}
							@mousemove=${this.#onStageMove}
							@mouseleave=${this.#onStageLeave}></div>`}
			</div>
		`;
	}

	protected renderOverlayUI() {
		const empty = this.isEmptyPreview;
		return html`
			<div class="ui-layer">
				${this._loading ? html`<div class="loader"><uui-loader></uui-loader></div>` : nothing}
				${this._structure || empty || (this.backdropMode && this._structure) ? this.#renderChip() : nothing}
				${this.#renderBadges()}
				<div class="toolbar">
					<uui-button-group>
						<uui-button
							compact
							look="secondary"
							label="Switch to native block cards"
							title="Switch all blocks to native cards — re-enable with the eye icon in the top bar or Ctrl+Shift+P"
							@click=${this.#onPreviewOff}>
							<umb-icon name="icon-eye"></umb-icon>
						</uui-button>
						<uui-button
							compact
							look=${this._structure ? 'primary' : 'secondary'}
							label="Toggle structure mode"
							title=${this._structure ? 'Hide structure (all blocks)' : 'Show structure: outlines + names for all blocks'}
							@click=${this.#onToggleStructure}>
							<umb-icon name="icon-grid"></umb-icon>
						</uui-button>
						<uui-button compact look="secondary" label="Zoom preview" title="Zoom preview" @click=${this.#onZoom}>
							<umb-icon name="icon-zoom-in"></umb-icon>
						</uui-button>
						<uui-button compact look="secondary" label="Refresh preview" title="Refresh preview" @click=${this.#onRefresh}>
							<umb-icon name="icon-refresh"></umb-icon>
						</uui-button>
						<uui-button
							compact
							look=${this._interactive ? 'primary' : 'secondary'}
							label="Toggle interaction"
							title=${this._interactive ? 'Disable interaction' : 'Interact with preview (carousels, hovers)'}
							@click=${this.#onToggleInteractive}>
							<umb-icon name="icon-hand-pointer-alt"></umb-icon>
						</uui-button>
					</uui-button-group>
				</div>
				${this._error ? html`<div class="error">${this._error}</div>` : nothing}
			</div>
		`;
	}

	#renderChip() {
		const size = this.sizeBadge;
		return html`<button class="chip" title="Edit block" @click=${this.#onHitClick}>
			<umb-icon name=${this.icon ?? 'icon-document'}></umb-icon>
			<span class="chip-label">${this.label ?? ''}</span>
			${size ? html`<span class="chip-size">${size}</span>` : nothing}
		</button>`;
	}

	/**
	 * Compact editing bar shown when an ancestor's preview already renders this block:
	 * click to edit, native action bar and drag/drop continue to work on the entry.
	 */
	protected renderGhostBar() {
		const size = this.sizeBadge;
		return html`<button class="ghost" title="Edit block" @click=${this.#onHitClick}>
			<umb-icon name=${this.icon ?? 'icon-document'}></umb-icon>
			<span class="ghost-label">${this.label ?? ''}</span>
			${size ? html`<span class="chip-size">${size}</span>` : nothing}
		</button>`;
	}

	#renderFrame(buffer: 'a' | 'b') {
		const doc = buffer === 'a' ? this._docA : this._docB;
		const isActive = this._activeBuffer === buffer;
		// Backdrop frames fill the host (chrome stretches to 100%); normal frames use content height.
		const height = this.backdropMode
			? Math.max(this._hostHeight / this.scale, 96)
			: Math.max(this._heights[buffer] || 0, 96);
		return html`<iframe
			id="frame-${buffer}"
			class="${isActive ? 'active' : ''} ${this._interactive && isActive ? 'interactive' : ''}"
			style="width:${this.designWidth}px;height:${Math.ceil(height)}px;transform:scale(${this.scale})"
			scrolling="no"
			title="Block preview"
			.srcdoc=${doc || ''}></iframe>`;
	}

	#renderBadges() {
		const invalid = this.contentInvalid || this.settingsInvalid;
		if (!invalid && !this.unpublished) return nothing;
		return html`<div class="badges">
			${invalid ? html`<uui-tag color="danger" look="primary">!</uui-tag>` : nothing}
			${this.unpublished ? html`<uui-tag color="warning" look="primary">unpublished</uui-tag>` : nothing}
		</div>`;
	}

	override render() {
		if (this.isGhost) return this.renderGhostBar();
		return html`${this.renderStage()}${this.renderOverlayUI()}`;
	}

	static override styles = [
		css`
			:host {
				display: block;
				position: relative;
			}

			.stage {
				position: relative;
				width: 100%;
				overflow: hidden;
				background: #fff;
				border-radius: var(--uui-border-radius, 3px);
				/* Low minimum so short blocks (spacers, dividers) keep their true height;
				   near-invisible previews get the labeled placeholder instead. */
				min-height: 24px;
				transition: height 0.12s ease-out;
			}

			.stage.transparent {
				background: transparent;
			}

			.stage.backdrop {
				position: absolute;
				inset: 0;
				min-height: 0;
				height: auto;
				z-index: 0;
			}

			.ui-layer {
				position: absolute;
				inset: 0;
				z-index: 4;
				pointer-events: none;
			}

			.ui-layer .toolbar,
			.ui-layer .chip {
				pointer-events: auto;
			}

			.stage.structure {
				outline: 2px dashed var(--uui-color-default, #3544b1);
				outline-offset: -2px;
				box-shadow: inset 0 0 0 200vmax rgba(53, 68, 177, 0.04);
			}

			.stage.empty {
				background: repeating-linear-gradient(
					-45deg,
					rgba(53, 68, 177, 0.03),
					rgba(53, 68, 177, 0.03) 10px,
					rgba(53, 68, 177, 0.08) 10px,
					rgba(53, 68, 177, 0.08) 20px
				);
				outline: 2px dashed rgba(53, 68, 177, 0.45);
				outline-offset: -2px;
			}

			.chip {
				position: absolute;
				top: 6px;
				left: 6px;
				z-index: 2;
				display: flex;
				align-items: center;
				gap: 6px;
				max-width: calc(100% - 120px);
				padding: 4px 9px;
				border-radius: 3px;
				background: var(--uui-color-surface, #fff);
				border: 1px solid var(--uui-color-default, #3544b1);
				box-shadow: 0 1px 3px rgba(0, 0, 0, 0.12);
				font-size: 12px;
				font-family: inherit;
				color: var(--uui-color-text, #060606);
				cursor: pointer;
				white-space: nowrap;
			}

			.chip:hover {
				background: var(--uui-color-surface-alt, #f5f5f5);
			}

			.ghost {
				display: flex;
				align-items: center;
				gap: 8px;
				width: 100%;
				box-sizing: border-box;
				padding: 8px 10px;
				background: var(--uui-color-surface, #fff);
				border: 1px dashed var(--uui-color-border-emphasis, #a1a1a1);
				border-radius: 3px;
				font-family: inherit;
				font-size: 12px;
				color: var(--uui-color-text, #060606);
				text-align: left;
				cursor: pointer;
			}

			.ghost:hover {
				border-color: var(--uui-color-default, #3544b1);
				background: var(--uui-color-surface-alt, #f5f5f5);
			}

			.ghost umb-icon {
				font-size: 14px;
				flex-shrink: 0;
			}

			.ghost-label {
				overflow: hidden;
				text-overflow: ellipsis;
				white-space: nowrap;
			}

			.chip umb-icon {
				font-size: 14px;
				flex-shrink: 0;
			}

			.chip-label {
				overflow: hidden;
				text-overflow: ellipsis;
			}

			.chip-size {
				color: var(--uui-color-text-alt, #68676b);
				font-variant-numeric: tabular-nums;
				flex-shrink: 0;
			}

			iframe {
				position: absolute;
				top: 0;
				left: 0;
				border: 0;
				background: transparent;
				transform-origin: 0 0;
				opacity: 0;
				pointer-events: none;
				display: block;
			}

			iframe.active {
				opacity: 1;
			}

			iframe.interactive {
				pointer-events: auto;
			}

			.hit {
				position: absolute;
				inset: 0;
				cursor: pointer;
				z-index: 1;
			}

			.loader {
				position: absolute;
				inset: 0;
				display: flex;
				align-items: center;
				justify-content: center;
				background: rgba(255, 255, 255, 0.55);
				z-index: 2;
				pointer-events: none;
			}

			.toolbar {
				position: absolute;
				top: 6px;
				right: 6px;
				z-index: 3;
				opacity: 0;
				transition: opacity 0.12s;
			}

			:host(:hover) .toolbar {
				opacity: 1;
			}

			.badges {
				position: absolute;
				top: 6px;
				left: 6px;
				z-index: 3;
				display: flex;
				gap: 4px;
			}

			.error {
				font-size: 12px;
				color: var(--uui-color-danger, #d42054);
				padding: 4px 8px;
			}

			umb-block-grid-areas-container {
				display: block;
			}
		`,
	];
}
