import { customElement } from '@umbraco-cms/backoffice/external/lit';
import { UMB_BLOCK_RTE_ENTRY_CONTEXT, UMB_BLOCK_RTE_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/block-rte';
import type { UmbBlockDataModel, UmbBlockExposeModel } from '@umbraco-cms/backoffice/block';
import { RippleBaseViewElement } from './ripple-base-view.element.js';
import type { RippleRenderRequest } from '../api.js';

/**
 * Rich Text Editor block custom view: live server-rendered preview.
 */
@customElement('ripple-rte-view')
export class RippleRteViewElement extends RippleBaseViewElement {
	protected readonly editorKind = 'rte';

	#entry?: typeof UMB_BLOCK_RTE_ENTRY_CONTEXT.TYPE;
	#contents?: UmbBlockDataModel[];
	#settingsData?: UmbBlockDataModel[];
	#exposes?: UmbBlockExposeModel[];

	constructor() {
		super();

		this.consumeContext(UMB_BLOCK_RTE_ENTRY_CONTEXT, (entry) => {
			this.#entry = entry;
		});

		this.consumeContext(UMB_BLOCK_RTE_MANAGER_CONTEXT, (manager) => {
			if (!manager) return;
			this.observe(manager.propertyAlias, (alias) => {
				this._propertyAlias = alias ?? '';
			}, 'ripplePropAlias');
			this.observe(manager.variantId, (variantId) => {
				this._culture = variantId?.culture ?? null;
			}, 'rippleVariant');
			this.observe(manager.exposes, (exposes) => {
				this.#exposes = exposes;
			}, 'rippleExposes');
			this.observe(manager.contents, (contents) => {
				const first = this.#contents === undefined;
				this.#contents = contents;
				if (first) this.requestRender();
			}, 'rippleContents');
			this.observe(manager.settings, (settingsData) => {
				this.#settingsData = settingsData;
			}, 'rippleSettingsData');
		});
	}

	protected buildRequest(): RippleRenderRequest | null {
		if (!this.contentKey || !this._documentTypeKey) return null;
		if (!this.#contents || !this.layout) return null;

		return {
			blockValue: {
				layout: { 'Umbraco.RichText': [this.layout] },
				contentData: this.#contents,
				settingsData: this.#settingsData ?? [],
				expose: this.#exposes ?? [],
			},
			documentKey: this._documentKey,
			documentTypeKey: this._documentTypeKey,
			propertyAlias: this._propertyAlias,
			culture: this._culture,
			contentKey: this.contentKey,
			settingsKey: this.layout?.settingsKey ?? null,
			includeAreas: false,
		};
	}

	protected openEdit(): void {
		this.#entry?.edit();
	}
}

export default RippleRteViewElement;

declare global {
	interface HTMLElementTagNameMap {
		'ripple-rte-view': RippleRteViewElement;
	}
}
