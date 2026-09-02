<script lang="ts">
	import { fms } from '$lib/fms.svelte';
	import type { FieldDeviceDiagnostics } from '$lib/fms.svelte';
	import {
		deviceSpecificValues,
		formatAgo,
		formatTimestamp,
		statusBadgeClasses
	} from '$lib/utils/formatters';

	const fieldDevices = $derived<FieldDeviceDiagnostics[]>(fms.matchState?.fieldDevices ?? []);
</script>

<div class="p-3">
	<div class="mb-3 flex items-center justify-between">
		<div class="text-xs text-slate-600 dark:text-slate-300">
			Connected devices: <span class="font-bold">{fieldDevices.length}</span>
		</div>
	</div>

	{#if fieldDevices.length === 0}
		<div
			class="rounded border border-slate-200 bg-slate-50 px-3 py-6 text-center text-sm text-slate-500 dark:border-slate-700 dark:bg-slate-900/60 dark:text-slate-400"
		>
			No field devices connected.
		</div>
	{:else}
		<div class="overflow-x-auto rounded border border-slate-200 dark:border-slate-700">
			<table class="min-w-365 divide-y divide-slate-200 text-left text-xs dark:divide-slate-700">
				<thead class="bg-slate-100 text-slate-600 dark:bg-slate-900 dark:text-slate-300">
					<tr>
						<th class="p-2 font-semibold">Name</th>
						<th class="p-2 font-semibold">Type</th>
						<th class="p-2 font-semibold">Status</th>
						<th class="p-2 font-semibold">Bypass</th>
						<th class="p-2 font-semibold">Last Reply Time</th>
						<th class="p-2 font-semibold">Last Seen</th>
						<th class="p-2 font-semibold">Device-Specific Values</th>
					</tr>
				</thead>
				<tbody class="divide-y divide-slate-200 bg-white dark:divide-slate-700 dark:bg-slate-800">
					{#each fieldDevices as device (device.id)}
						<tr class="align-top hover:bg-slate-50 dark:hover:bg-slate-700/50">
							<td class="p-2 font-semibold text-slate-900 dark:text-slate-100">{device.name}</td>
							<td class="p-2 text-slate-700 dark:text-slate-300">{device.type}</td>
							<td class="p-2">
								<span
									class="rounded px-2 py-0.5 text-[10px] font-bold {statusBadgeClasses(
										device.status
									)}">{device.status}</span
								>
							</td>
							<td class="p-2">
								<input
									type="checkbox"
									checked={device.bypassed}
									onchange={() => fms.bypassFieldDevice(device.id, !device.bypassed)}
									class="size-4 cursor-pointer rounded border-slate-300 dark:border-slate-600"
								/>
							</td>
							<td class="p-2 text-slate-700 dark:text-slate-300">
								{#if device.replyTimeStats.sampleCount === 0}
									<span class="text-slate-400">No samples</span>
								{:else}
									<div class="font-semibold text-slate-900 dark:text-slate-100">
										{device.lastReplyTimeMs.toFixed(1)} ms
									</div>
									<div class="text-[10px] text-slate-500 dark:text-slate-400">
										avg: {device.replyTimeStats.avgMs.toFixed(1)} ms | min: {device.replyTimeStats.minMs.toFixed(
											1
										)} ms | max: {device.replyTimeStats.maxMs.toFixed(1)} ms
									</div>
								{/if}
							</td>
							<td class="p-2 text-[11px] text-slate-700 dark:text-slate-300">
								<div>{formatTimestamp(device.lastSeenUtc)}</div>
								<div class="text-slate-500 dark:text-slate-400">
									{formatAgo(device.secondsSinceLastSeen)}
								</div>
							</td>
							<td class="p-2">
								<div class="flex flex-wrap gap-2 text-[11px]">
									{#each deviceSpecificValues(device) as item}
										<div class="rounded bg-slate-100 px-2 py-1 dark:bg-slate-900">
											<span class="font-bold text-slate-600 dark:text-slate-400">{item.label}:</span
											>
											<span class="text-slate-900 dark:text-slate-100">{item.value}</span>
										</div>
									{/each}
								</div>
							</td>
						</tr>
					{/each}
				</tbody>
			</table>
		</div>
	{/if}
</div>
