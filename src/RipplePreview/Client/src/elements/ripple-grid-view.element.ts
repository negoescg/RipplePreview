import { css, html, customElement, nothing, state } from '@umbraco-cms/backoffice/external/lit';
import {
	UMB_BLOCK_GRID_ENTRIES_CONTEXT,
	UMB_BLOCK_GRID_ENTRY_CONTEXT,
	UMB_BLOCK_GRID_MANAGER_CONTEXT,
	type UmbBlockGridLayoutModel,
} from '@umbraco-cms/backoffice/block-grid';
import type { UmbBlockDataModel, UmbBlockExposeModel } from '@umbraco-cms/backoffice/block';
import { observeMultiple } from '@umbraco-cms/backoffice/observable-api';
import { RippleBaseViewElement } from './ripple-base-view.element.js';
import type { RippleRenderRequest } from '../api.js';
import { getRippleSettings } from '../settings.js';
import './ripple-ghost-boundary.element.js';

/**
 * Block Grid custom view: live server-rendered preview. Blocks with areas keep their
 * areas natively editable below the preview (umb-block-grid-areas-container) unless the
 * element type is configured for full-area previews.
 */
@customElement('ripple-grid-view')
export class RippleGridViewElement extends RippleBaseViewElement {
	protected readonly editorKind = 'grid';

	#entry?: typeof UMB_BLOCK_GRID_ENTRY_CONTEXT.TYPE;
	#layouts?: UmbBlockGridLayoutModel[];
	#contents?: UmbBlockDataModel[];
	#settingsData?: UmbBlockDataModel[];
	#exposes?: UmbBlockExposeModel[];
	#contentTypeAlias = '';
	#settingsKey: string | null = null;

	@state()
	private _nested = false;

	@state()
	private _areasExpanded = false;

	constructor() {
		super();

		this.consumeContext(UMB_BLOCK_GRID_ENTRIES_CONTEXT, (entries) => {
			// Inside an area (not the root list) — render on a transparent stage so the
			// parent's backdrop shows through.
			this._nested = !!entries?.getParentUnique?.();
		});

		this.consumeContext(UMB_BLOCK_GRID_ENTRY_CONTEXT, (entry) => {
			this.#entry = entry;
			if (!entry) return;
			this.observe(
				observeMultiple([entry.layout, entry.contentElementTypeAlias]),
				([layout, alias]) => {
					this.#settingsKey = layout?.settingsKey ?? null;
					this.#contentTypeAlias = alias ?? '';
					this.requestRender();
				},
				'rippleGridEntry',
			);
		});

		this.consumeContext(UMB_BLOCK_GRID_MANAGER_CONTEXT, (manager) => {
			if (!manager) return;
			this.observe(manager.propertyAlias, (alias) => {
				this._propertyAlias = alias ?? '';
			}, 'ripplePropAlias');
			this.observe(manager.variantId, (variantId) => {
				this._culture = variantId?.culture ?? null;
			}, 'rippleVariant');
			this.observe(manager.layouts, (layouts) => {
				// Layout tree changes (resize, move, area changes) affect width fractions and areas.
				this.#layouts = layouts as UmbBlockGridLayoutModel[];
				this.requestRender();
			}, 'rippleLayouts');
			this.observe(manager.exposes, (exposes) => {
				this.#exposes = exposes;
			}, 'rippleExposes');
			this.observe(manager.contents, (contents) => {
				const first = this.#contents === undefined;
				this.#contents = contents;
				// Sibling edits arrive here too — only nested-children edits matter for our preview,
				// and only when areas render inside the iframe. Own edits arrive via `content` prop.
				if (first || this.includeAreas) this.requestRender();
			}, 'rippleContents');
			this.observe(manager.settings, (settingsData) => {
				const first = this.#settingsData === undefined;
				this.#settingsData = settingsData;
				if (first || this.includeAreas) this.requestRender();
			}, 'rippleSettingsData');
		});
	}

	protected get hasAreas(): boolean {
		const typeAreas = (this.blockType as { areas?: unknown[] } | undefined)?.areas;
		return Array.isArray(typeAreas) && typeAreas.length > 0;
	}

	/**
	 * How this parent block presents its areas:
	 * - 'full' (default): one document renders the block and all its children exactly like
	 *   the frontend; a compact editing strip below keeps children natively editable.
	 * - 'stacked' (StackedAreaPreviewContentTypes): the block's own chrome previews on its
	 *   own; children render as individual previews below.
	 * - 'fullSolo' (FullAreaPreviewContentTypes): full preview without the editing strip;
	 *   children are edited through their own workspaces.
	 */
	protected get areaMode(): 'none' | 'full' | 'stacked' | 'fullSolo' {
		if (!this.hasAreas) return 'none';
		const alias = this.#contentTypeAlias || (this.blockType as { contentElementTypeAlias?: string } | undefined)?.contentElementTypeAlias || '';
		const gridSettings = getRippleSettings().blockGrid;
		if (alias && gridSettings.fullAreaPreviewContentTypes?.includes(alias)) return 'fullSolo';
		if (alias && gridSettings.stackedAreaPreviewContentTypes?.includes(alias)) return 'stacked';
		return 'full';
	}

	protected get includeAreas(): boolean {
		return this.areaMode === 'full' || this.areaMode === 'fullSolo';
	}

	protected override get isNested(): boolean {
		return this._nested;
	}

	protected override get supportsChildHitTesting(): boolean {
		return this.includeAreas && this.childCount > 0;
	}

	/** Total child blocks across this block's areas. */
	protected get childCount(): number {
		const areas = (this.layout as { areas?: Array<{ items?: unknown[] }> } | undefined)?.areas;
		return areas?.reduce((count, area) => count + (area.items?.length ?? 0), 0) ?? 0;
	}

	#onToggleAreas(event: Event) {
		event.stopPropagation();
		this._areasExpanded = !this._areasExpanded;
	}

	protected buildRequest(): RippleRenderRequest | null {
		if (!this.contentKey || !this._propertyAlias || !this._documentTypeKey) return null;
		if (!this.#layouts || !this.#contents) return null;

		return {
			blockValue: {
				layout: { 'Umbraco.BlockGrid': this.#layouts },
				contentData: this.#contents,
				settingsData: this.#settingsData ?? [],
				expose: this.#exposes ?? [],
			},
			documentKey: this._documentKey,
			documentTypeKey: this._documentTypeKey,
			propertyAlias: this._propertyAlias,
			culture: this._culture,
			contentKey: this.contentKey,
			settingsKey: this.#settingsKey ?? this.layout?.settingsKey ?? null,
			includeAreas: this.includeAreas,
		};
	}

	protected openEdit(): void {
		this.#entry?.edit();
	}

	protected override get sizeBadge(): string | null {
		const layout = this.layout as { columnSpan?: number; rowSpan?: number } | undefined;
		if (!layout) return null;
		return `${layout.columnSpan ?? 12} × ${layout.rowSpan ?? 1}`;
	}

	override render() {
		if (this.isGhost) {
			// Already shown inside an ancestor's preview — compact bar plus native areas
			// so deeper nesting stays editable.
			return html`
				${this.renderGhostBar()}
				${this.hasAreas
					? html`<umb-block-grid-areas-container draggable="false"></umb-block-grid-areas-container>`
					: nothing}
			`;
		}

		const mode = this.areaMode;
		if (mode === 'stacked') {
			return html`
				${this.renderStage()}
				<ripple-ghost-boundary .enabled=${false}>
					<umb-block-grid-areas-container draggable="false"></umb-block-grid-areas-container>
				</ripple-ghost-boundary>
				${this.renderOverlayUI()}
			`;
		}

		if (mode === 'full') {
			// The preview already shows the children; the editing strip stays collapsed
			// until requested (or while global structure mode is on). With no children yet,
			// keep it open so the native "add content" buttons are reachable.
			const expanded = this._areasExpanded || this._structure || this.childCount === 0;
			return html`
				${this.renderStage()}
				${expanded
					? html`<button class="strip-toggle expanded" title="Hide the editing strip" @click=${this.#onToggleAreas}>
							<umb-icon name="icon-grid"></umb-icon>
							<span>Hide blocks</span>
						</button>`
					: html`<button class="strip-toggle pill" title="Edit, reorder or add child blocks" @click=${this.#onToggleAreas}>
							<umb-icon name="icon-grid"></umb-icon>
							<span>Edit blocks (${this.childCount})</span>
						</button>`}
				<ripple-ghost-boundary .enabled=${true} class=${expanded ? '' : 'collapsed'}>
					<umb-block-grid-areas-container draggable="false"></umb-block-grid-areas-container>
				</ripple-ghost-boundary>
				${this.renderOverlayUI()}
			`;
		}

		return html`${this.renderStage()}${this.renderOverlayUI()}`;
	}

	static override styles = [
		...RippleBaseViewElement.styles,
		css`
			umb-block-grid-areas-container {
				position: relative;
				display: block;
				margin-top: var(--uui-size-1, 3px);
			}

			ripple-ghost-boundary.collapsed {
				display: none;
			}

			.strip-toggle {
				display: flex;
				align-items: center;
				gap: 6px;
				box-sizing: border-box;
				padding: 4px 10px;
				background: var(--uui-color-surface, #fff);
				border: 1px dashed var(--uui-color-border, #d8d7d9);
				border-radius: 3px;
				font-family: inherit;
				font-size: 11px;
				color: var(--uui-color-text-alt, #68676b);
				text-align: left;
				cursor: pointer;
			}

			/* Collapsed: a floating pill in the preview's corner — takes no layout space. */
			.strip-toggle.pill {
				position: absolute;
				left: 6px;
				bottom: 6px;
				width: auto;
				z-index: 4;
				border-radius: 20px;
				box-shadow: 0 1px 3px rgba(0, 0, 0, 0.15);
				opacity: 0;
				transition: opacity 0.12s;
			}

			:host(:hover) .strip-toggle.pill,
			.strip-toggle.pill:focus-visible {
				opacity: 1;
			}

			.strip-toggle.expanded {
				width: 100%;
				margin-top: 2px;
			}

			.strip-toggle:hover {
				border-color: var(--uui-color-default, #3544b1);
				color: var(--uui-color-text, #060606);
			}

			.strip-toggle umb-icon {
				font-size: 12px;
			}
		`,
	];
}

export default RippleGridViewElement;

declare global {
	interface HTMLElementTagNameMap {
		'ripple-grid-view': RippleGridViewElement;
	}
}
