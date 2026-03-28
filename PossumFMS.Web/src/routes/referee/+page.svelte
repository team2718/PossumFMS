<script lang="ts">
	import { fms } from '$lib/fms.svelte';
	import Navbar from '$lib/Navbar.svelte';
	import type { MatchViolation, Station, ViolationType } from '$lib/fms.svelte';

	interface Penalty {
		name: string;
		rule: string;
		type: ViolationType;
	}

	const penalties: Penalty[] = [
        { name: "G403: Limited AUTO opponent interaction", rule: "G403", type: 'MajorFoul'},
        { name: "G405: Keep SCORING ELEMENTS in bounds", rule: "G405", type: 'MinorFoul' },
        { name: "G407: Only score while in your ALLIANCE ZONE.", rule: "G407", type: 'MajorFoul'},
        { name: "G408: Don't catch FUEL", rule: "G408", type: 'MinorFoul' },
		{ name: "G415: Stay out of other ROBOTS", rule: "G415", type: 'MinorFoul' },
        { name: "G416: This isn't combat robotics", rule: "G416", type: 'MajorFoul' },
        { name: "G417: Don't tip or entangle", rule: "G417", type: 'MajorFoul' },
        { name: "G418: There's a 3-count on PINS", rule: "G418", type: 'MinorFoul' },
		{ name: "G420: TOWER protection", rule: "G420", type: 'MajorFoul' },
	];

	$effect(() => {
		fms.connect();
	});

	const matchState = $derived(fms.matchState);
	const phase = $derived(matchState?.phase ?? 'Disconnected');
	const violations = $derived<MatchViolation[]>(matchState?.violations ?? []);
	const editablePhases = ['Auto', 'AutoToTeleopTransition', 'Teleop', 'PostMatch'];
	const canEditViolations = $derived(editablePhases.includes(phase));
	const redStations = $derived<Station[]>(matchState?.stations.slice(0, 3) ?? []);
	const blueStations = $derived<Station[]>(matchState?.stations.slice(3, 6) ?? []);

	let selectedPenaltyIndex = $state(0);
	let refereeWarning = $state('');

	const selectedPenalty = $derived(penalties[selectedPenaltyIndex]);

	function formatStationLabel(station: Station): string {
		return `${station.alliance} ${station.position}`;
	}

	function formatMatchClock(violation: MatchViolation): string {
		const totalSeconds = Math.max(0, Math.ceil(violation.timeRemainingSeconds));
		const minutes = Math.floor(totalSeconds / 60);
		const seconds = totalSeconds % 60;
		return `${violation.phase} ${minutes}:${seconds.toString().padStart(2, '0')}`;
	}

	function stationDetail(station: Station): string {
		return station.teamNumber > 0 ? `Team ${station.teamNumber}` : 'No team assigned';
	}

	async function addViolation(stationIndex: number) {
		refereeWarning = '';
		try {
			await fms.addViolation(stationIndex, selectedPenalty.rule);
		} catch (error) {
			refereeWarning =
				error instanceof Error
					? error.message
					: 'Failed to record violation. Please try again.';
		}
	}

	async function removeViolation(violationId: string) {
		refereeWarning = '';
		try {
			await fms.removeViolation(violationId);
		} catch (error) {
			refereeWarning =
				error instanceof Error
					? error.message
					: 'Failed to delete violation. Please try again.';
		}
	}
</script>

{#snippet stationCard(station: Station)}
	<button
		onclick={() => addViolation(station.index)}
		disabled={!canEditViolations}
		class="flex min-h-40 w-full flex-col items-start justify-between rounded-2xl border-2 bg-white px-5 py-5 text-left shadow-md transition active:translate-y-px active:shadow-sm disabled:cursor-not-allowed disabled:opacity-40 {station.alliance ===
		'Red'
			? 'border-rose-300 hover:border-rose-400'
			: 'border-blue-300 hover:border-blue-400'}"
	>
		<div>
			<div class="text-sm font-black tracking-widest text-slate-500 uppercase">
				{formatStationLabel(station)}
			</div>
			<div class="mt-2 text-4xl font-black text-slate-900">
				{station.teamNumber > 0 ? station.teamNumber : '----'}
			</div>
			<div class="mt-2 text-base font-semibold text-slate-600">{stationDetail(station)}</div>
		</div>
	</button>
{/snippet}

<div class="app-neutral-bg min-h-screen text-slate-900">
	<Navbar />

	<main class="mx-auto flex max-w-425 flex-col gap-4 px-3 py-4">
		<div class="flex flex-wrap items-center gap-3 rounded border border-slate-200 bg-white px-4 py-3 shadow-sm">
			<span class="text-base font-black text-slate-800">Referee Panel</span>
			<span
				class="rounded-full border px-3 py-1 text-sm font-semibold {canEditViolations
					? 'border-emerald-300 bg-emerald-50 text-emerald-800'
					: 'border-slate-300 bg-slate-50 text-slate-600'}"
			>
				{phase}
			</span>
			{#if matchState}
				<span class="text-sm text-slate-500">{matchState.matchType} #{matchState.matchNumber}</span>
			{/if}
			{#if !canEditViolations}
				<span class="text-sm font-semibold text-amber-700">
					Violations can be edited only during the match or in PostMatch.
				</span>
			{/if}
		</div>

		<div class="rounded border border-slate-200 bg-white p-4 shadow-sm">
			<div class="mb-3 text-sm font-black tracking-widest text-slate-500 uppercase">
				Selected Violation
			</div>
			<select
				bind:value={selectedPenaltyIndex}
				class="w-full rounded-xl border-2 border-slate-300 bg-white px-4 py-4 text-2xl font-black text-slate-900"
			>
				{#each penalties as penalty, index}
					<option value={index}>{penalty.name}</option>
				{/each}
			</select>
		</div>

		{#if refereeWarning}
			<div class="rounded border border-rose-300 bg-rose-50 px-4 py-3 text-base font-semibold text-rose-700">
				{refereeWarning}
			</div>
		{/if}

		{#if !matchState}
			<div class="rounded border border-slate-200 bg-white px-4 py-10 text-center text-base text-slate-500 shadow-sm">
				Waiting for FMS connection…
			</div>
		{:else}
			<div class="grid grid-cols-1 gap-4 xl:grid-cols-[1.35fr_1fr]">
				<div class="grid grid-cols-1 gap-4">
					<div class="rounded border border-rose-200 bg-rose-50/60 p-4 shadow-sm">
						<div class="mb-3 text-lg font-black tracking-widest text-rose-700 uppercase">Red Alliance</div>
						<div class="grid grid-cols-1 gap-3 md:grid-cols-3">
							{#each redStations as station}
								{@render stationCard(station)}
							{/each}
						</div>
					</div>

					<div class="rounded border border-blue-200 bg-blue-50/60 p-4 shadow-sm">
						<div class="mb-3 text-lg font-black tracking-widest text-blue-700 uppercase">Blue Alliance</div>
						<div class="grid grid-cols-1 gap-3 md:grid-cols-3">
							{#each blueStations as station}
								{@render stationCard(station)}
							{/each}
						</div>
					</div>
				</div>

				<div class="rounded border border-slate-200 bg-white p-4 shadow-sm">
					<div class="mb-3 flex items-center justify-between gap-3">
						<div>
							<div class="text-lg font-black tracking-widest text-slate-800 uppercase">
								Match Violations
							</div>
							<div class="text-sm font-semibold text-slate-500">
								Most recent first
							</div>
						</div>
						<div class="rounded-full bg-slate-100 px-3 py-1 text-sm font-bold text-slate-700">
							{violations.length}
						</div>
					</div>

					<div class="max-h-[60vh] space-y-3 overflow-y-auto pr-1">
						{#if violations.length === 0}
							<div class="rounded-xl border border-dashed border-slate-300 bg-slate-50 px-4 py-8 text-center text-base text-slate-500">
								No fouls recorded for this match.
							</div>
						{:else}
							{#each violations as violation}
								<div class="rounded-2xl border border-slate-200 bg-slate-50 p-4">
									<div class="flex items-start justify-between gap-3">
										<div>
											<div class="text-sm font-black tracking-widest text-slate-500 uppercase">
												{formatMatchClock(violation)}
											</div>
											<div class="mt-1 text-2xl font-black text-slate-900">
												{violation.rule} - {violation.type === 'MajorFoul' ? 'Major' : 'Minor'}
											</div>
											<div class="mt-2 text-base font-semibold text-slate-700">
												{violation.alliance} {violation.position} {violation.teamNumber > 0
													? `• Team ${violation.teamNumber}`
													: '• No team assigned'}
											</div>
										</div>
										<button
											onclick={() => removeViolation(violation.id)}
											disabled={!canEditViolations}
											class="min-h-18 min-w-28 rounded-xl border-2 border-rose-300 bg-white px-4 py-3 text-lg font-black text-rose-700 transition hover:bg-rose-50 disabled:cursor-not-allowed disabled:opacity-40"
										>
											Delete
										</button>
									</div>
								</div>
							{/each}
						{/if}
					</div>
				</div>
			</div>
		{/if}
	</main>
</div>