import type { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';

export interface RippleEditorSettings {
	enabled: boolean;
	contentTypes: string[];
	fullAreaPreviewContentTypes: string[];
	stackedAreaPreviewContentTypes: string[];
}

export interface RippleSettings {
	designWidth: number;
	blockGrid: RippleEditorSettings;
	blockList: RippleEditorSettings;
	richText: RippleEditorSettings;
	singleBlock: RippleEditorSettings;
}

export type RippleEditorKind = 'grid' | 'list' | 'rte' | 'single';

export interface RippleRenderRequest {
	blockValue: unknown;
	documentKey: string | null;
	documentTypeKey: string;
	propertyAlias: string;
	culture: string | null;
	contentKey: string;
	settingsKey: string | null;
	includeAreas: boolean;
}

export interface RippleRenderResponse {
	html: string;
	widthFraction: number;
}

const API_BASE = '/umbraco/ripple-preview/api/v1';

/**
 * IMPORTANT: requests go through a plain fetch with an explicitly awaited bearer token
 * (UMB_AUTH_CONTEXT.getLatestToken refreshes when needed). Never use the shared
 * umbHttpClient here — a tokenless 401 on it trips the backoffice's global
 * session-timeout handler and logs the user out.
 */
let authContext: typeof UMB_AUTH_CONTEXT.TYPE | undefined;

export function setAuthContext(context: typeof UMB_AUTH_CONTEXT.TYPE) {
	authContext = context;
}

async function authFetch<T>(url: string, init?: RequestInit): Promise<{ data?: T; error?: unknown }> {
	try {
		const token = await authContext?.getLatestToken();
		if (!token) return { error: 'No backoffice token available.' };

		const response = await fetch(url, {
			...init,
			headers: {
				'Content-Type': 'application/json',
				Authorization: `Bearer ${token}`,
				...init?.headers,
			},
		});

		if (!response.ok) return { error: `HTTP ${response.status}` };
		return { data: (await response.json()) as T };
	} catch (error) {
		return { error };
	}
}

export async function getSettings(): Promise<RippleSettings | undefined> {
	const { data } = await authFetch<RippleSettings>(`${API_BASE}/settings`);
	return data;
}

export async function postRender(
	editor: RippleEditorKind,
	request: RippleRenderRequest,
): Promise<{ data?: RippleRenderResponse; error?: unknown }> {
	return authFetch<RippleRenderResponse>(`${API_BASE}/render/${editor}`, {
		method: 'POST',
		body: JSON.stringify(request),
	});
}
