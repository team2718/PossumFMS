<script lang="ts">
	import { fms } from '$lib/fms.svelte';
	import type { LogSeverity, RecentLogEntry } from '$lib/fms.svelte';
	import { formatLogTimestamp, logEntryClasses } from '$lib/utils/formatters';

	let logSearch = $state('');
	const logSeverityOptions: LogSeverity[] = [
		'Trace',
		'Debug',
		'Information',
		'Warning',
		'Error',
		'Critical'
	];
	let selectedSeverities = $state<Record<LogSeverity, boolean>>({
		Trace: true,
		Debug: true,
		Information: true,
		Warning: true,
		Error: true,
		Critical: true
	});

	const filteredLogs = $derived<RecentLogEntry[]>(
		fms.logEntries.filter((entry) => {
			if (!selectedSeverities[entry.level]) return false;
			if (!logSearch.trim()) return true;

			const term = logSearch.toLowerCase();
			return (
				entry.message.toLowerCase().includes(term) ||
				entry.category.toLowerCase().includes(term) ||
				entry.level.toLowerCase().includes(term)
			);
		})
	);

	function toggleAllSeverities(enable: boolean) {
		for (const level of logSeverityOptions) {
			selectedSeverities[level] = enable;
		}
	}
</script>

<div class="p-3">
	<div
		class="mb-3 flex flex-wrap items-end justify-between gap-3 rounded border border-slate-200 bg-slate-50 p-3"
	>
		<div class="min-w-[260px] flex-1">
			<label for="log-search" class="mb-1 block text-xs font-semibold text-slate-600"
				>Search Logs</label
			>
			<input
				id="log-search"
				type="text"
				bind:value={logSearch}
				placeholder="Keyword, category, or message"
				class="w-full rounded border border-slate-300 bg-white px-3 py-1.5 text-xs text-slate-800 focus:border-slate-500 focus:outline-hidden"
			/>
		</div>
		<div class="flex flex-wrap items-center gap-3">
			<div class="text-xs font-semibold text-slate-600">Severity:</div>
			{#each logSeverityOptions as level}
				<label class="inline-flex items-center gap-1 text-xs text-slate-700">
					<input
						type="checkbox"
						bind:checked={selectedSeverities[level]}
						class="h-3.5 w-3.5 cursor-pointer rounded border-slate-300"
					/>
					<span>{level}</span>
				</label>
			{/each}
			<div class="flex items-center gap-1 border-l border-slate-300 pl-2">
				<button
					type="button"
					onclick={() => toggleAllSeverities(true)}
					class="cursor-pointer rounded px-2 py-1 text-[11px] font-semibold text-slate-600 hover:bg-slate-200"
				>
					All
				</button>
				<button
					type="button"
					onclick={() => toggleAllSeverities(false)}
					class="cursor-pointer rounded px-2 py-1 text-[11px] font-semibold text-slate-600 hover:bg-slate-200"
				>
					None
				</button>
			</div>
		</div>
	</div>

	<div class="max-h-[500px] overflow-y-auto rounded border border-slate-200 bg-white">
		{#if filteredLogs.length === 0}
			<div class="p-6 text-center text-xs text-slate-500">
				{#if fms.logEntries.length === 0}
					No recent log entries received yet.
				{:else}
					No logs match the current search or severity filter.
				{/if}
			</div>
		{:else}
			<table class="w-full text-left text-[11px]">
				<thead class="sticky top-0 border-b border-slate-200 bg-slate-100 text-slate-600">
					<tr>
						<th class="w-36 px-2 py-1.5 font-semibold">Timestamp</th>
						<th class="w-24 px-2 py-1.5 font-semibold">Level</th>
						<th class="w-48 px-2 py-1.5 font-semibold">Category</th>
						<th class="px-2 py-1.5 font-semibold">Message</th>
					</tr>
				</thead>
				<tbody class="divide-y divide-slate-100">
					{#each filteredLogs as entry (entry.id)}
						<tr class="hover:bg-slate-50">
							<td class="px-2 py-1.5 font-mono text-[10px] text-slate-500 whitespace-nowrap">
								{formatLogTimestamp(entry.timestampUtc)}
							</td>
							<td class="px-2 py-1.5">
								<span
									class="inline-block rounded border px-1.5 py-0.5 text-[10px] font-bold uppercase {logEntryClasses(
										entry.level
									)}"
								>
									{entry.level}
								</span>
							</td>
							<td class="max-w-[200px] truncate px-2 py-1.5 font-mono text-[10px] text-slate-600">
								{entry.category}
							</td>
							<td class="px-2 py-1.5 font-mono text-[11px] text-slate-800">
								{entry.message}
							</td>
						</tr>
					{/each}
				</tbody>
			</table>
		{/if}
	</div>
</div>

