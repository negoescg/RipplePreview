/**
 * Global "structure mode": when on, every Ripple preview shows its outline and a
 * name/size chip so editors can see the rows/cells skeleton of the page at a glance.
 * Toggled from any block's toolbar; persisted per browser.
 */
const STORAGE_KEY = 'ripple:structure-mode';

let enabled = false;
try {
	enabled = localStorage.getItem(STORAGE_KEY) === '1';
} catch {
	// storage unavailable — session-only toggle
}

const listeners = new Set<(value: boolean) => void>();

export function isStructureMode(): boolean {
	return enabled;
}

export function toggleStructureMode(): boolean {
	enabled = !enabled;
	try {
		localStorage.setItem(STORAGE_KEY, enabled ? '1' : '0');
	} catch {
		// ignore
	}
	listeners.forEach((listener) => listener(enabled));
	return enabled;
}

export function onStructureModeChange(listener: (value: boolean) => void): () => void {
	listeners.add(listener);
	return () => listeners.delete(listener);
}
