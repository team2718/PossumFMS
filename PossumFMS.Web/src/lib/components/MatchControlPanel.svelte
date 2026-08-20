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

<div
	class="app-neutral-bg rounded border border-slate-300 p-3 shadow-xs transition-colors dark:border-slate-700"
>
	<div class="flex flex-col gap-3">
		{#if matchControlWarning}
			<div
				class="rounded border border-rose-300 bg-rose-100 px-3 py-1.5 text-center text-xs font-bold text-rose-800 dark:border-rose-800 dark:bg-rose-950/80 dark:text-rose-300"
			>
				{matchControlWarning}
			</div>
		{/if}
		{#if commitResultsMessage}
			<div
				class="rounded border px-3 py-1.5 text-center text-xs font-bold {commitResultsMessage.success
					? 'border-emerald-300 bg-emerald-100 text-emerald-800 dark:border-emerald-800 dark:bg-emerald-950/80 dark:text-emerald-300'
					: 'border-rose-300 bg-rose-100 text-rose-800 dark:border-rose-800 dark:bg-rose-950/80 dark:text-rose-300'}"
			>
				{commitResultsMessage.text}
			</div>
		{/if}

		<!-- Row 1: Grouped Lifecycle & Emergency Actions -->
		<div class="flex flex-wrap items-center justify-center gap-4">
			<!-- Normal Match Lifecycle Group -->
			<div
				class="flex flex-wrap items-center justify-center gap-2 rounded-lg border border-slate-200 bg-white/60 p-1.5 shadow-xs dark:border-slate-700/80 dark:bg-slate-900/40"
			>
				<button
					type="button"
					onclick={() => {
						fms.startPreMatch();
						fms.setAudienceView('live');
					}}
					disabled={phase !== 'Idle' ||
						!!matchState?.arenaEstop ||
						!!matchState?.freePracticeEnabled}
					class="h-13 min-w-38 cursor-pointer rounded px-4 text-xs font-black tracking-wide uppercase shadow-xs transition disabled:cursor-not-allowed disabled:opacity-40 {phase ===
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
					class="h-13 min-w-38 cursor-pointer rounded px-4 text-xs font-black tracking-wide uppercase shadow-xs transition disabled:cursor-not-allowed {phase ===
						'PreMatch' &&
					blueReady &&
					redReady &&
					!matchState?.arenaEstop
						? 'animate-pulse bg-emerald-600 text-white hover:bg-emerald-500'
						: 'bg-[repeating-linear-gradient(-45deg,#b9bec8_0px,#b9bec8_8px,#a9aeb8_8px,#a9aeb8_16px)] text-slate-600 dark:text-slate-400'}"
				>
					Start Match
				</button>
				<button
					type="button"
					onclick={commitMatchResults}
					disabled={!canCommit}
					class="h-13 min-w-38 cursor-pointer rounded px-4 text-xs font-black tracking-wide uppercase shadow-xs transition disabled:cursor-not-allowed disabled:opacity-40 {canCommit
						? 'bg-indigo-600 text-white hover:bg-indigo-500'
						: 'bg-indigo-950 text-slate-400'}"
				>
					{isCommittingResults ? 'Committing…' : 'Commit Results'}
				</button>
				<button
					type="button"
					onclick={() => fms.clearMatch()}
					disabled={(phase !== 'PostMatch' && phase !== 'PreMatch' && phase !== 'Idle') ||
						!!matchState?.arenaEstop}
					class="h-13 min-w-32 cursor-pointer rounded bg-slate-500 px-4 text-xs font-black tracking-wide text-white uppercase shadow-xs transition hover:bg-slate-400 disabled:cursor-not-allowed disabled:opacity-40"
				>
					Clear
				</button>
			</div>

			<!-- Divider -->
			<div class="hidden h-10 w-px bg-slate-300 lg:block dark:bg-slate-700"></div>

			<!-- Emergency / Abort Safety Group -->
			<div
				class="flex flex-wrap items-center justify-center gap-2 rounded-lg border border-rose-200 bg-rose-50/40 p-1.5 shadow-xs dark:border-rose-900/40 dark:bg-rose-950/20"
			>
				<button
					type="button"
					onclick={() => fms.abortMatch()}
					disabled={phase !== 'Auto' && phase !== 'AutoToTeleopTransition' && phase !== 'Teleop'}
					class="h-13 min-w-38 cursor-pointer rounded px-4 text-xs font-black tracking-wide text-white uppercase shadow-xs transition disabled:cursor-not-allowed disabled:opacity-40 {phase ===
						'Auto' ||
					phase === 'AutoToTeleopTransition' ||
					phase === 'Teleop'
						? 'bg-orange-600 ring-2 ring-orange-400 hover:bg-orange-500'
						: 'bg-orange-950/80 text-slate-400'}"
				>
					Abort Match
				</button>

				{#if matchState?.arenaEstop}
					<button
						type="button"
						onclick={() => void resetArenaEstop()}
						class="h-13 min-w-38 cursor-pointer rounded bg-[repeating-linear-gradient(-45deg,#e7ca4f_0px,#e7ca4f_8px,#9a9a9a_8px,#9a9a9a_16px)] px-4 text-xs font-black tracking-wide text-black uppercase shadow-xs transition hover:opacity-90"
					>
						Reset Arena E-Stop
					</button>
				{:else}
					<button
						type="button"
						onclick={() => fms.triggerArenaEstop()}
						class="h-13 min-w-38 cursor-pointer rounded bg-rose-700 px-4 text-xs font-black tracking-wide text-white uppercase shadow-xs transition hover:bg-rose-600 active:scale-95"
					>
						Arena E-STOP
					</button>
				{/if}
			</div>
		</div>

		<!-- Row 2: Audience Display Controls -->
		<div
			class="flex flex-wrap items-center justify-center gap-2 border-t border-slate-200 pt-2.5 dark:border-slate-700/80"
		>
			<span
				class="mr-1 text-[11px] font-bold tracking-wider text-slate-500 uppercase dark:text-slate-400"
			>
				Audience View:
			</span>
			<div
				class="inline-flex rounded-lg border border-slate-300 bg-slate-100 p-0.5 shadow-xs dark:border-slate-600 dark:bg-slate-800"
			>
				<button
					type="button"
					onclick={() => fms.setAudienceView('blank')}
					class="cursor-pointer rounded-md px-3.5 py-1.5 text-xs font-bold transition-colors {audienceView ===
					'blank'
						? 'bg-slate-800 text-white shadow-xs dark:bg-slate-200 dark:text-slate-900'
						: 'text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-white'}"
				>
					Blank
				</button>
				<button
					type="button"
					onclick={() => fms.setAudienceView('live')}
					class="cursor-pointer rounded-md px-3.5 py-1.5 text-xs font-bold transition-colors {audienceView ===
					'live'
						? 'bg-teal-700 text-white shadow-xs'
						: 'text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-white'}"
				>
					Match Play
				</button>
				<button
					type="button"
					onclick={() => fms.setAudienceView('matchResults')}
					disabled={!hasCommittedResults}
					class="cursor-pointer rounded-md px-3.5 py-1.5 text-xs font-bold transition-colors disabled:cursor-not-allowed disabled:opacity-40 {audienceView ===
					'matchResults'
						? 'bg-teal-700 text-white shadow-xs'
						: 'text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-white'}"
				>
					Match Results
				</button>
			</div>
		</div>
	</div>
</div>
