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
		<div class="text-xs text-slate-600">
			Connected devices: <span class="font-bold">{fieldDevices.length}</span>
		</div>
	</div>

	{#if fieldDevices.length === 0}
		<div
			class="rounded border border-slate-200 bg-slate-50 px-3 py-6 text-center text-sm text-slate-500"
		>
			No field devices connected.
		</div>
	{:else}
		<div class="overflow-x-auto rounded border border-slate-200">
			<table class="min-w-[1460px] divide-y divide-slate-200 text-left text-xs">
				<thead class="bg-slate-100 text-slate-600">
					<tr>
						<th class="px-2 py-2 font-semibold">Name</th>
						<th class="px-2 py-2 font-semibold">Type</th>
						<th class="px-2 py-2 font-semibold">Status</th>
						<th class="px-2 py-2 font-semibold">Bypass</th>
						<th class="px-2 py-2 font-semibold">Last Reply Time</th>
						<th class="px-2 py-2 font-semibold">Last Seen</th>
						<th class="px-2 py-2 font-semibold">Device-Specific Values</th>
					</tr>
				</thead>
				<tbody class="divide-y divide-slate-200 bg-white">
					{#each fieldDevices as device (device.id)}
						<tr class="align-top hover:bg-slate-50">
							<td class="px-2 py-2 font-semibold text-slate-900">{device.name}</td>
							<td class="px-2 py-2 text-slate-700">{device.type}</td>
							<td class="px-2 py-2">
								<span
									class="rounded px-2 py-0.5 text-[10px] font-bold {statusBadgeClasses(
										device.status
									)}">{device.status}</span
								>
							</td>
							<td class="px-2 py-2">
								<input
									type="checkbox"
									checked={device.bypassed}
									onchange={() => fms.bypassFieldDevice(device.id, !device.bypassed)}
									class="h-4 w-4 cursor-pointer rounded border-slate-300"
								/>
							</td>
							<td class="px-2 py-2 text-slate-700">
								{#if device.replyTimeStats.sampleCount === 0}
									<span class="text-slate-400">No samples</span>
								{:else}
									<div class="font-semibold text-slate-900">
										{device.lastReplyTimeMs.toFixed(1)} ms
									</div>
									<div class="text-[10px] text-slate-500">
										avg: {device.replyTimeStats.avgMs.toFixed(1)} ms | min: {device.replyTimeStats.minMs.toFixed(
											1
										)} ms | max: {device.replyTimeStats.maxMs.toFixed(1)} ms
									</div>
								{/if}
							</td>
							<td class="px-2 py-2 text-[11px] text-slate-700">
								<div>{formatTimestamp(device.lastSeenUtc)}</div>
								<div class="text-slate-500">{formatAgo(device.secondsSinceLastSeen)}</div>
							</td>
							<td class="px-2 py-2">
								<div class="flex flex-wrap gap-2 text-[11px]">
									{#each deviceSpecificValues(device) as item}
										<div class="rounded bg-slate-100 px-2 py-1">
											<span class="font-bold text-slate-600">{item.label}:</span>
											<span class="text-slate-900">{item.value}</span>
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

