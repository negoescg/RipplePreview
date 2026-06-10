/**
 * Limits the number of concurrent preview render requests so pages with many
 * blocks don't overwhelm the server (or starve the browser connection pool).
 */
const MAX_CONCURRENT = 4;

let active = 0;
const waiting: Array<() => void> = [];

export async function enqueue<T>(task: () => Promise<T>): Promise<T> {
	if (active >= MAX_CONCURRENT) {
		await new Promise<void>((resolve) => waiting.push(resolve));
	}
	active++;
	try {
		return await task();
	} finally {
		active--;
		waiting.shift()?.();
	}
}
