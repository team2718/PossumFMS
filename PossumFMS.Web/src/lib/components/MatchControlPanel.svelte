<script lang="ts">
	import { fms } from '$lib/fms.svelte';

	let matchControlWarning = $state('');
	let isCommittingResults = $state(false);
	let commitResultsMessage = $state<{ success: boolean; text: string } | null>(null);

	const matchState = $derived(fms.matchState);
	const phase = $derived(matchState?.phase ?? 'Disconnected');

	const redStations = $derived(matchState?.stations.slice(0, 3) ?? []);
	const blueStations = $derived(matchState?.stations.slice(3, 6) ?? []);

	const isMatchStartingOrRunning = $derived(
		phase === 'PreMatch' ||
			phase === 'Auto' ||
			phase === 'AutoToTeleopTransition' ||
			phase === 'Teleop'
	);

	const redReady = $derived(
		redStations.length === 3 &&
			redStations.every((s) => (isMatchStartingOrRunning ? s.isReadyInMatch : s.isReady))
	);

	const blueReady = $derived(
		blueStations.length === 3 &&
			blueStations.every((s) => (isMatchStartingOrRunning ? s.isReadyInMatch : s.isReady))
	);

	const audienceView = $derived(matchState?.audienceView ?? 'live');
	const canCommit = $derived(
		phase === 'PostMatch' &&
			!isCommittingResults &&
			!!matchState?.matchId &&
			matchState?.lastCommittedMatch?.matchId !== matchState?.matchId
	);
	const hasCommittedResults = $derived(matchState?.lastCommittedMatch != null);

	async function startMatch() {
		matchControlWarning = '';
		try {
			await fms.startMatch();
		} catch (error) {
			matchControlWarning = error instanceof Error ? error.message : 'Unable to start the match.';
		}
	}

	async function resetArenaEstop() {
		matchControlWarning = '';
		try {
			await fms.resetArenaEstop();
		} catch (error) {
			matchControlWarning =
				error instanceof Error ? error.message : 'Unable to reset arena E-stop.';
		}
	}

	async function commitMatchResults() {
		commitResultsMessage = null;
		isCommittingResults = true;
		try {
			await fms.commitMatchResults();
			commitResultsMessage = { success: true, text: 'Match results committed.' };
		} catch (error) {
			commitResultsMessage = {
				success: false,
				text: error instanceof Error ? error.message : 'Failed to commit results.'
			};
		} finally {
			isCommittingResults = false;
		}
	}
</script>

<div class="app-neutral-bg rounded border border-slate-300 p-3 shadow-xs">
	<div class="flex flex-col gap-2">
		{#if matchControlWarning}
			<div class="rounded bg-rose-100 px-3 py-1.5 text-center text-xs font-bold text-rose-800">
				{matchControlWarning}
			</div>
		{/if}
		{#if commitResultsMessage}
			<div
				class="rounded px-3 py-1.5 text-center text-xs font-bold {commitResultsMessage.success
					? 'bg-emerald-100 text-emerald-800'
					: 'bg-rose-100 text-rose-800'}"
			>
				{commitResultsMessage.text}
			</div>
		{/if}

		<!-- Row 1: Match lifecycle -->
		<div class="flex flex-wrap items-center justify-center gap-3">
			<button
				type="button"
				onclick={() => {
					fms.startPreMatch();
					fms.setAudienceView('live');
				}}
				disabled={phase !== 'Idle' ||
					!!matchState?.arenaEstop ||
					!!matchState?.freePracticeEnabled}
				class="h-14 min-w-44 cursor-pointer rounded px-5 text-sm font-black transition disabled:cursor-not-allowed disabled:opacity-40 {phase ===
					'Idle' && !matchState?.freePracticeEnabled
					? 'bg-amber-400 text-slate-900 hover:bg-amber-300'
					: 'bg-amber-800 text-white'}"
			>
				Prestart Match
			</button>
			<button
				type="button"
				onclick={() => void startMatch()}
				disabled={phase !== 'PreMatch' || !blueReady || !redReady || !!matchState?.arenaEstop}
				class="h-14 min-w-44 cursor-pointer rounded px-5 text-sm font-black transition disabled:cursor-not-allowed {phase ===
					'PreMatch' &&
				blueReady &&
				redReady &&
				!matchState?.arenaEstop
					? 'bg-emerald-700 text-white hover:bg-emerald-600'
					: 'bg-[repeating-linear-gradient(-45deg,#b9bec8_0px,#b9bec8_8px,#a9aeb8_8px,#a9aeb8_16px)] text-slate-500'}"
			>
				Start Match
			</button>
			<button
				type="button"
				onclick={() => fms.abortMatch()}
				disabled={phase !== 'Auto' && phase !== 'AutoToTeleopTransition' && phase !== 'Teleop'}
				class="h-14 min-w-44 cursor-pointer rounded px-5 text-sm font-black text-white transition disabled:cursor-not-allowed disabled:opacity-40 {phase ===
					'Auto' ||
				phase === 'AutoToTeleopTransition' ||
				phase === 'Teleop'
					? 'bg-orange-600 hover:bg-orange-500'
					: 'bg-orange-900'}"
			>
				Abort Match
			</button>
			<button
				type="button"
				onclick={commitMatchResults}
				disabled={!canCommit}
				class="h-14 min-w-44 cursor-pointer rounded px-5 text-sm font-black text-white transition disabled:cursor-not-allowed disabled:opacity-40 {canCommit
					? 'bg-indigo-700 hover:bg-indigo-600'
					: 'bg-indigo-950'}"
			>
				{isCommittingResults ? 'Committing…' : 'Commit Results'}
			</button>
			<button
				type="button"
				onclick={() => fms.clearMatch()}
				disabled={(phase !== 'PostMatch' && phase !== 'PreMatch' && phase !== 'Idle') ||
					!!matchState?.arenaEstop}
				class="h-14 min-w-44 cursor-pointer rounded bg-slate-500 px-5 text-sm font-black text-white transition hover:bg-slate-400 disabled:cursor-not-allowed disabled:opacity-40"
			>
				Clear
			</button>
			{#if matchState?.arenaEstop}
				<button
					type="button"
					onclick={() => void resetArenaEstop()}
					class="h-14 min-w-44 cursor-pointer rounded bg-[repeating-linear-gradient(-45deg,#e7ca4f_0px,#e7ca4f_8px,#9a9a9a_8px,#9a9a9a_16px)] px-5 text-sm font-black text-black transition"
				>
					Reset Arena E-Stop
				</button>
			{:else}
				<button
					type="button"
					onclick={() => fms.triggerArenaEstop()}
					class="h-14 min-w-44 cursor-pointer rounded bg-rose-700 px-5 text-sm font-black text-white transition hover:bg-rose-600"
				>
					Arena E-STOP
				</button>
			{/if}
		</div>

		<!-- Row 2: Display controls -->
		<div class="flex flex-wrap items-center justify-center gap-3">
			<button
				type="button"
				onclick={() => fms.setAudienceView('blank')}
				class="h-14 min-w-44 cursor-pointer rounded border px-5 text-sm font-black transition-colors {audienceView ===
				'blank'
					? 'border-slate-900 bg-slate-800 text-white ring-2 ring-white'
					: 'border-slate-400 bg-slate-300 text-slate-600 hover:bg-slate-200'}"
			>
				Show Blank
			</button>
			<button
				type="button"
				onclick={() => fms.setAudienceView('live')}
				class="h-14 min-w-44 cursor-pointer rounded border px-5 text-sm font-black transition-colors {audienceView ===
				'live'
					? 'border-teal-900 bg-teal-700 text-white ring-2 ring-white'
					: 'border-slate-400 bg-slate-300 text-slate-600 hover:bg-slate-200'}"
			>
				Show Match Play
			</button>
			<button
				type="button"
				onclick={() => fms.setAudienceView('matchResults')}
				disabled={!hasCommittedResults}
				class="h-14 min-w-44 cursor-pointer rounded border px-5 text-sm font-black transition-colors disabled:cursor-not-allowed disabled:opacity-40 {audienceView ===
				'matchResults'
					? 'border-teal-900 bg-teal-700 text-white ring-2 ring-white'
					: 'border-slate-400 bg-slate-300 text-slate-600 hover:bg-slate-200'}"
			>
				Show Match Results
			</button>
		</div>
	</div>
</div>

