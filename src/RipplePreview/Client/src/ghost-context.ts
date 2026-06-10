import { UmbContextToken } from '@umbraco-cms/backoffice/context-api';

/**
 * Provided by a parent block view that renders its children inside its own preview.
 * Descendant block views consume it and render as compact editing bars ("ghosts")
 * instead of fetching their own previews — the parent's document already shows them.
 */
export interface RippleGhostAreasContext {
	getHostElement(): Element;
	readonly enabled: boolean;
}

export const RIPPLE_GHOST_AREAS_CONTEXT = new UmbContextToken<RippleGhostAreasContext>('RipplePreview.GhostAreas');
