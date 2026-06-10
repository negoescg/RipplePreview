/**
 * Global preview on/off: when off, Ripple's custom views are unregistered and block
 * editors fall back to Umbraco's native entry rendering. Persisted per browser.
 */
const STORAGE_KEY = 'ripple:preview-mode';

let enabled = true;
try {
	enabled = localStorage.getItem(STORAGE_KEY) !== '0';
} catch {
	// storage unavailable — session-only toggle
}

const listeners = new Set<(value: boolean) => void>();

export function isPreviewMode(): boolean {
	return enabled;
}

export function togglePreviewMode(): boolean {
	enabled = !enabled;
	try {
		localStorage.setItem(STORAGE_KEY, enabled ? '1' : '0');
	} catch {
		// ignore
	}
	listeners.forEach((listener) => listener(enabled));
	return enabled;
}

export function onPreviewModeChange(listener: (value: boolean) => void): () => void {
	listeners.add(listener);
	return () => listeners.delete(listener);
}
