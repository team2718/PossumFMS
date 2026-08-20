import type { FieldDeviceDiagnostics, LogSeverity } from '$lib/fms.svelte';

/**
 * Formats a duration in seconds into M:SS (e.g. 2:05)
 */
export function formatTime(secs: number): string {
	const total = Math.max(0, Math.ceil(secs));
	const m = Math.floor(total / 60);
	const s = total % 60;
	return `${m}:${s.toString().padStart(2, '0')}`;
}

/**
 * Formats last robot link time into a human-readable string
 */
export function formatLastRobotLink(secondsSinceLink: number, robotLinked: boolean): string {
	if (robotLinked) return 'Now';
	if (secondsSinceLink >= 300) return '>5 mins ago';

	const wholeSeconds = Math.max(0, Math.round(secondsSinceLink));
	return `${wholeSeconds} ${wholeSeconds === 1 ? 'second' : 'seconds'} ago`;
}

/**
 * Formats an ISO date string to local locale string
 */
export function formatTimestamp(value: string): string {
	const timestamp = new Date(value);
	if (Number.isNaN(timestamp.getTime())) return '—';
	return timestamp.toLocaleString();
}

/**
 * Formats a log entry timestamp to local locale string
 */
export function formatLogTimestamp(value: string): string {
	const timestamp = new Date(value);
	if (Number.isNaN(timestamp.getTime())) return value;
	return timestamp.toLocaleString();
}

/**
 * Formats seconds elapsed to human-readable ago format (<1s ago, 5s ago, 2m ago, etc.)
 */
export function formatAgo(seconds: number): string {
	if (seconds < 1) return '<1s ago';
	if (seconds < 60) return `${Math.round(seconds)}s ago`;
	if (seconds < 3600) return `${Math.floor(seconds / 60)}m ${Math.round(seconds % 60)}s ago`;
	const hours = Math.floor(seconds / 3600);
	const minutes = Math.round((seconds % 3600) / 60);
	return `${hours}h ${minutes}m ago`;
}

/**
 * Returns station label string given 0-indexed station index
 */
export function stationLabel(idx: number): string {
	return idx < 3 ? `Red ${idx + 1}` : `Blue ${idx - 2}`;
}

/**
 * Returns CSS classes for status badges (Connected, Error, Disconnected)
 */
export function statusBadgeClasses(status: string): string {
	return status === 'Connected'
		? 'bg-emerald-100 text-emerald-800'
		: status === 'Error'
			? 'bg-rose-100 text-rose-800'
			: 'bg-slate-200 text-slate-700';
}

/**
 * Returns CSS classes for log entry rows based on severity
 */
export function logEntryClasses(level: LogSeverity): string {
	return level === 'Critical'
		? 'border-red-950 bg-red-950 text-white'
		: level === 'Error'
			? 'border-rose-300 bg-rose-100 text-rose-950'
			: level === 'Warning'
				? 'border-amber-300 bg-amber-100 text-amber-950'
				: level === 'Information'
					? 'border-sky-200 bg-sky-50 text-sky-950'
					: level === 'Debug'
						? 'border-slate-300 bg-slate-100 text-slate-900'
						: 'border-slate-200 bg-white text-slate-700';
}

/**
 * Formats device-specific key-value pairs from heartbeat diagnostics
 */
export function deviceSpecificValues(
	device: FieldDeviceDiagnostics
): Array<{ label: string; value: string }> {
	if (!device.heartbeat) return [{ label: 'Values', value: 'No parsed heartbeat yet' }];

	if (device.heartbeat.kind === 'Hub') {
		return [
			{ label: 'Alliance', value: device.heartbeat.alliance.toUpperCase() },
			{ label: 'Fuel Count', value: String(device.heartbeat.fuelCount) },
			{ label: 'Heartbeat', value: formatTimestamp(device.heartbeat.receivedUtc) }
		];
	}

	return [
		{ label: 'Field', value: device.heartbeat.field.toUpperCase() },
		{ label: 'Station', value: String(device.heartbeat.station) },
		{ label: 'A-Stop', value: device.heartbeat.astopActivated ? 'Active' : 'Clear' },
		{ label: 'E-Stop', value: device.heartbeat.estopActivated ? 'Active' : 'Clear' },
		{ label: 'Heartbeat', value: formatTimestamp(device.heartbeat.receivedUtc) }
	];
}

