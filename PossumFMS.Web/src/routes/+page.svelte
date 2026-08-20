<script lang="ts">
	import { fms } from '$lib/fms.svelte';
	import Navbar from '$lib/Navbar.svelte';
	import OperatorAuthModal from '$lib/components/OperatorAuthModal.svelte';
	import AccessPointModal from '$lib/components/AccessPointModal.svelte';
	import StationGrid from '$lib/components/StationGrid.svelte';
	import StationCard from '$lib/components/StationCard.svelte';
	import MatchControlPanel from '$lib/components/MatchControlPanel.svelte';
	import ScoringControlPanel from '$lib/components/ScoringControlPanel.svelte';
	import FieldHardwarePanel from '$lib/components/FieldHardwarePanel.svelte';
	import LogConsole from '$lib/components/LogConsole.svelte';
	import EstopSafetyModal from '$lib/components/EstopSafetyModal.svelte';

	// Connect to the FMS hub when the page loads
	$effect(() => {
		fms.connect();
	});

	// Local inputs for team number and WPA key — one per station (index 0–5)
	let inputs = $state(Array.from({ length: 6 }, () => ({ team: '', wpa: '' })));

	// Pre-fill inputs from the first MatchState broadcast so existing assignments show up
	let hasInitialized = $state(false);
	$effect(() => {
		if (!hasInitialized && fms.matchState?.stations) {
			for (let i = 0; i < 6; i++) {
				const t = fms.matchState.stations[i].teamNumber;
				if (t > 0) inputs[i].team = String(t);
			}
			hasInitialized = true;
		}
	});

	const matchState = $derived(fms.matchState);
	const phase = $derived(matchState?.phase ?? 'Disconnected');

	const displayPhase = $derived(
		(() => {
			switch (phase) {
				case 'PreMatch':
					return 'Pre-Match';
				case 'Auto':
					return 'Autonomous';
				case 'AutoToTeleopTransition':
					return 'Transition';
				case 'Teleop':
					return 'Teleop';
				case 'PostMatch':
					return 'Post-Match';
				case 'Idle':
					return 'Idle';
				default:
					return phase;
			}
		})()
	);

	let activeTab = $state<'score' | 'status' | 'field' | 'options' | 'event' | 'log'>('score');

	let isWpaModalOpen = $state(false);
	let isAuthModalOpen = $state(false);
	let isEstopSafetyModalOpen = $state(false);
	let isTogglingEstopCheck = $state(false);

	let isConfiguring = $state(false);
	let configureWarning = $state('');
	let configureSuccess = $state('');

	// Options tab state
	let optionsWarning = $state('');
	let optionsSuccess = $state('');
	let isTogglingFreePractice = $state(false);
	let isSavingMatchDurations = $state(false);
	let autoDurationSecondsInput = $state('20');
	let autoToTeleopTransitionDurationSecondsInput = $state('3');
	let teleopDurationSecondsInput = $state('140');
	let hasInitializedMatchDurations = $state(false);

	// Event tab state
	let tbaEventKeyInput = $state('');
	let isLoadingTeams = $state(false);
	let teamLoadResult = $state<{ success: boolean; message: string } | null>(null);

	const redStations = $derived(matchState?.stations.slice(0, 3) ?? []);
	const blueStations = $derived(matchState?.stations.slice(3, 6) ?? []);

	const allianceOrderSetting = $derived<'blueLeft' | 'redLeft'>(
		(matchState?.allianceOrder as 'blueLeft' | 'redLeft') ?? 'blueLeft'
	);

	$effect(() => {
		if (!matchState?.matchDurations) return;

		if (!hasInitializedMatchDurations && !isSavingMatchDurations) {
			autoDurationSecondsInput = String(matchState.matchDurations.autoSeconds);
			autoToTeleopTransitionDurationSecondsInput = String(
				matchState.matchDurations.autoToTeleopTransitionSeconds
			);
			teleopDurationSecondsInput = String(matchState.matchDurations.teleopSeconds);
			hasInitializedMatchDurations = true;
		}
	});

	async function setFreePracticeEnabled(enabled: boolean) {
		optionsWarning = '';
		optionsSuccess = '';
		isTogglingFreePractice = true;

		try {
			await fms.setFreePracticeEnabled(enabled);
		} catch (error) {
			optionsWarning =
				error instanceof Error
					? error.message
					: 'Failed to update Free Practice. Please try again.';
		} finally {
			isTogglingFreePractice = false;
		}
	}

	function handleToggleEstopCheck(enabled: boolean) {
		if (!enabled) {
			isEstopSafetyModalOpen = true;
			return;
		}
		void setRequireFieldEstop(true);
	}

	async function setRequireFieldEstop(required: boolean) {
		optionsWarning = '';
		optionsSuccess = '';
		isTogglingEstopCheck = true;

		try {
			await fms.setRequireFieldEstopForMatchStart(required);
			optionsSuccess = required
				? 'Field hardware E-Stop requirement enabled (Standard safety mode).'
				: 'Field hardware E-Stop requirement disabled (Offline testing mode).';
		} catch (error) {
			optionsWarning =
				error instanceof Error
					? error.message
					: 'Failed to update field E-Stop requirement. Please try again.';
		} finally {
			isTogglingEstopCheck = false;
		}
	}

	function parseNonNegativeSeconds(value: string): number | null {
		const parsed = Number(value);
		if (!Number.isFinite(parsed) || parsed < 0) return null;
		return parsed;
	}

	async function saveMatchDurations() {
		optionsWarning = '';
		optionsSuccess = '';

		if (phase !== 'Idle') {
			optionsWarning = 'Match durations can only be changed while the arena is idle.';
			return;
		}

		const autoSeconds = parseNonNegativeSeconds(autoDurationSecondsInput);
		const transitionSeconds = parseNonNegativeSeconds(autoToTeleopTransitionDurationSecondsInput);
		const teleopSeconds = parseNonNegativeSeconds(teleopDurationSecondsInput);

		if (autoSeconds === null || transitionSeconds === null || teleopSeconds === null) {
			optionsWarning = 'All durations must be non-negative numbers (0 is allowed).';
			return;
		}

		isSavingMatchDurations = true;

		try {
			await fms.setMatchDurations(autoSeconds, transitionSeconds, teleopSeconds);
			optionsSuccess = 'Match durations updated.';
		} catch (error) {
			optionsWarning =
				error instanceof Error
					? error.message
					: 'Failed to update match durations. Please try again.';
		} finally {
			isSavingMatchDurations = false;
		}
	}

	async function loadTeamsFromTba() {
		teamLoadResult = null;
		isLoadingTeams = true;
		try {
			const result = await fms.loadTeamsFromTba(tbaEventKeyInput.trim());
			teamLoadResult = {
				success: true,
				message: `Loaded ${result.newTeamsCount} teams from ${tbaEventKeyInput.trim()}. Database now has ${result.totalTeamsCount} teams total.`
			};
		} catch (error) {
			teamLoadResult = {
				success: false,
				message: error instanceof Error ? error.message : 'Failed to load teams.'
			};
		} finally {
			isLoadingTeams = false;
		}
	}
</script>

<div class="app-neutral-bg min-h-screen text-slate-900 transition-colors dark:text-slate-100">
	<Navbar />

	<main class="mx-auto flex max-w-[1700px] flex-col gap-3 px-3 py-3">
		<!-- Station Grid and Readiness Matrix -->
		<StationGrid
			bind:inputs
			bind:isConfiguring
			bind:configureWarning
			bind:configureSuccess
			onOpenWpaModal={() => (isWpaModalOpen = true)}
		/>

		<!-- Match Lifecycle Control Panel -->
		<MatchControlPanel />

		<!-- Tab Navigation & Panels -->
		<div
			class="rounded border border-slate-300 bg-white shadow-xs transition-colors dark:border-slate-700 dark:bg-slate-800"
		>
			<div
				class="flex items-center gap-0 border-b border-slate-300 px-3 pt-2 text-sm dark:border-slate-700"
			>
				<button
					type="button"
					onclick={() => (activeTab = 'score')}
					class="mr-6 cursor-pointer pb-2 {activeTab === 'score'
						? 'brand-secondary-border brand-secondary-text border-b-2 font-semibold'
						: 'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'}"
				>
					Score
				</button>
				<button
					type="button"
					onclick={() => (activeTab = 'status')}
					class="mr-6 cursor-pointer pb-2 {activeTab === 'status'
						? 'brand-secondary-border brand-secondary-text border-b-2 font-semibold'
						: 'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'}"
				>
					Status
				</button>
				<button
					type="button"
					onclick={() => (activeTab = 'field')}
					class="mr-6 cursor-pointer pb-2 {activeTab === 'field'
						? 'brand-secondary-border brand-secondary-text border-b-2 font-semibold'
						: 'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'}"
				>
					Field
				</button>
				<button
					type="button"
					onclick={() => (activeTab = 'options')}
					class="mr-6 cursor-pointer pb-2 {activeTab === 'options'
						? 'brand-secondary-border brand-secondary-text border-b-2 font-semibold'
						: 'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'}"
				>
					Options
				</button>
				<button
					type="button"
					onclick={() => (activeTab = 'event')}
					class="mr-6 cursor-pointer pb-2 {activeTab === 'event'
						? 'brand-secondary-border brand-secondary-text border-b-2 font-semibold'
						: 'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'}"
				>
					Event
				</button>
				<button
					type="button"
					onclick={() => (activeTab = 'log')}
					class="mr-6 cursor-pointer pb-2 {activeTab === 'log'
						? 'brand-secondary-border brand-secondary-text border-b-2 font-semibold'
						: 'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'}"
				>
					Log
				</button>
			</div>

			<!-- Tab 1: Score -->
			{#if activeTab === 'score'}
				<ScoringControlPanel />

				<!-- Tab 2: Detailed Station Status Cards -->
			{:else if activeTab === 'status'}
				<div class="p-3">
					<div class="grid grid-cols-1 gap-3 md:grid-cols-2">
						<!-- Blue Alliance -->
						<div class="alliance-blue-border-soft alliance-blue-bg-soft rounded border p-3">
							<div class="alliance-blue-text mb-3 text-xs font-bold tracking-wider uppercase">
								Blue Alliance
							</div>
							{#each blueStations as s, i (s.index)}
								<StationCard station={s} stationNumber={i + 1} alliance="blue" />
							{/each}
						</div>

						<!-- Red Alliance -->
						<div class="alliance-red-border-soft alliance-red-bg-soft rounded border p-3">
							<div class="alliance-red-text mb-3 text-xs font-bold tracking-wider uppercase">
								Red Alliance
							</div>
							{#each redStations as s, i (s.index)}
								<StationCard station={s} stationNumber={3 - i} alliance="red" />
							{/each}
						</div>
					</div>
				</div>

				<!-- Tab 3: Field Hardware Telemetry -->
			{:else if activeTab === 'field'}
				<FieldHardwarePanel />

				<!-- Tab 4: Field Options & Configuration -->
			{:else if activeTab === 'options'}
				<div class="p-3">
					{#if phase !== 'Idle'}
						<div
							class="mb-3 flex items-center gap-2 rounded-lg border border-amber-300 bg-amber-50 px-3.5 py-2.5 text-xs font-semibold text-amber-900 shadow-xs dark:border-amber-700/80 dark:bg-amber-950/70 dark:text-amber-300"
						>
							<svg
								xmlns="http://www.w3.org/2000/svg"
								class="h-4 w-4 shrink-0 text-amber-600 dark:text-amber-400"
								fill="none"
								viewBox="0 0 24 24"
								stroke="currentColor"
							>
								<path
									stroke-linecap="round"
									stroke-linejoin="round"
									stroke-width="2"
									d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z"
								/>
							</svg>
							<span
								>Arena options are locked during match play (<strong>{displayPhase}</strong>).
								Return the arena to <strong>Idle</strong> to modify field settings.</span
							>
						</div>
					{/if}

					{#if optionsWarning}
						<div
							class="mb-3 rounded border border-rose-300 bg-rose-50 px-3 py-2 text-xs font-semibold text-rose-700 dark:border-rose-700 dark:bg-rose-950/60 dark:text-rose-300"
						>
							{optionsWarning}
						</div>
					{/if}
					{#if optionsSuccess}
						<div
							class="mb-3 rounded border border-emerald-300 bg-emerald-50 px-3 py-2 text-xs font-semibold text-emerald-700 dark:border-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-300"
						>
							{optionsSuccess}
						</div>
					{/if}
					<div class="flex flex-wrap gap-3">
						<!-- Free Practice -->
						<div
							class="min-w-[320px] rounded border px-4 py-3 transition-all {phase !== 'Idle'
								? 'border-dashed border-slate-300 bg-slate-100/60 opacity-60 dark:border-slate-700 dark:bg-slate-900/40'
								: 'border-slate-200 bg-slate-50 dark:border-slate-700 dark:bg-slate-900/60'}"
						>
							<div class="flex items-start justify-between gap-4">
								<div>
									<div
										class="flex items-center gap-1.5 text-sm font-bold text-slate-900 dark:text-slate-100"
									>
										<span>Free Practice</span>
										{#if phase !== 'Idle'}
											<span class="text-[10px] text-slate-400 dark:text-slate-500">🔒</span>
										{/if}
									</div>
									<div class="mt-1 max-w-xl text-xs text-slate-600 dark:text-slate-400">
										Stops FMS communication to driver stations while leaving AP configuration and
										Hub counting available.
									</div>
								</div>
								<label
									class="flex items-center gap-2 text-sm font-semibold text-slate-800 dark:text-slate-200 {phase !==
									'Idle'
										? 'cursor-not-allowed'
										: 'cursor-pointer'}"
								>
									<input
										type="checkbox"
										checked={matchState?.freePracticeEnabled ?? false}
										disabled={!matchState || phase !== 'Idle' || isTogglingFreePractice}
										onchange={(event) =>
											setFreePracticeEnabled((event.currentTarget as HTMLInputElement).checked)}
										class="h-4 w-4 cursor-pointer disabled:cursor-not-allowed"
									/>
									<span>{matchState?.freePracticeEnabled ? 'Enabled' : 'Disabled'}</span>
								</label>
							</div>
							<div class="mt-3 text-[11px] font-medium text-slate-500 dark:text-slate-400">
								{phase === 'Idle'
									? 'Free Practice can be toggled while the arena is idle.'
									: '🔒 Locked: Return the arena to Idle before changing Free Practice.'}
							</div>
						</div>

						<!-- Field Hardware E-Stop Safety Check -->
						<div
							class="min-w-[340px] rounded border px-4 py-3 transition-all {phase !== 'Idle'
								? 'border-dashed border-slate-300 bg-slate-100/60 opacity-60 dark:border-slate-700 dark:bg-slate-900/40'
								: 'border-slate-200 bg-slate-50 dark:border-slate-700 dark:bg-slate-900/60'}"
						>
							<div class="flex items-start justify-between gap-4">
								<div>
									<div
										class="flex items-center gap-2 text-sm font-bold text-slate-900 dark:text-slate-100"
									>
										<span>Field Hardware E-Stop Check</span>
										{#if matchState?.requireFieldEstopForMatchStart === false}
											<span
												class="rounded bg-amber-100 px-2 py-0.5 text-[10px] font-black text-amber-800 uppercase dark:bg-amber-950 dark:text-amber-300"
											>
												Testing Mode
											</span>
										{/if}
										{#if phase !== 'Idle'}
											<span class="text-[10px] text-slate-400 dark:text-slate-500">🔒</span>
										{/if}
									</div>
									<div class="mt-1 max-w-xl text-xs text-slate-600 dark:text-slate-400">
										Enforces that at least one healthy physical field E-Stop is connected before
										matches can start.
									</div>
								</div>
								<label
									class="flex items-center gap-2 text-sm font-semibold text-slate-800 dark:text-slate-200 {phase !==
									'Idle'
										? 'cursor-not-allowed'
										: 'cursor-pointer'}"
								>
									<input
										type="checkbox"
										checked={matchState?.requireFieldEstopForMatchStart ?? true}
										disabled={!matchState || phase !== 'Idle' || isTogglingEstopCheck}
										onchange={(event) =>
											handleToggleEstopCheck((event.currentTarget as HTMLInputElement).checked)}
										class="h-4 w-4 cursor-pointer disabled:cursor-not-allowed"
									/>
									<span
										>{(matchState?.requireFieldEstopForMatchStart ?? true)
											? 'Enabled'
											: 'Disabled'}</span
									>
								</label>
							</div>
							<div
								class="mt-3 text-[11px] font-medium {matchState?.requireFieldEstopForMatchStart ===
								false
									? 'font-bold text-amber-700 dark:text-amber-400'
									: 'text-slate-500 dark:text-slate-400'}"
							>
								{matchState?.requireFieldEstopForMatchStart === false
									? '⚠️ Safety check is disabled. Matches can start without hardware E-Stops.'
									: phase === 'Idle'
										? 'Hardware E-Stop check is active for live matches.'
										: '🔒 Locked: Return the arena to Idle before changing E-Stop requirement.'}
							</div>
						</div>

						<!-- Match Durations -->
						<div
							class="min-w-[420px] rounded border px-4 py-3 transition-all {phase !== 'Idle'
								? 'border-dashed border-slate-300 bg-slate-100/60 opacity-60 dark:border-slate-700 dark:bg-slate-900/40'
								: 'border-slate-200 bg-slate-50 dark:border-slate-700 dark:bg-slate-900/60'}"
						>
							<div
								class="flex items-center gap-1.5 text-sm font-bold text-slate-900 dark:text-slate-100"
							>
								<span>Match Durations</span>
								{#if phase !== 'Idle'}
									<span class="text-[10px] text-slate-400 dark:text-slate-500">🔒</span>
								{/if}
							</div>
							<div class="mt-1 text-xs text-slate-600 dark:text-slate-400">
								Configure Auto, Auto to Teleop transition, and Teleop durations in seconds. Enter 0
								for instant progression.
							</div>
							<div class="mt-3 grid grid-cols-3 gap-3">
								<label class="text-xs font-semibold text-slate-700 dark:text-slate-300">
									<span>Auto (s)</span>
									<input
										type="number"
										min="0"
										step="1"
										bind:value={autoDurationSecondsInput}
										disabled={phase !== 'Idle' || isSavingMatchDurations}
										class="mt-1 w-full rounded border border-slate-300 bg-white px-2 py-1 text-sm text-slate-900 disabled:cursor-not-allowed disabled:bg-slate-100 disabled:opacity-60 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100 dark:disabled:bg-slate-900/60"
									/>
								</label>
								<label class="text-xs font-semibold text-slate-700 dark:text-slate-300">
									<span>Auto→Teleop (s)</span>
									<input
										type="number"
										min="0"
										step="1"
										bind:value={autoToTeleopTransitionDurationSecondsInput}
										disabled={phase !== 'Idle' || isSavingMatchDurations}
										class="mt-1 w-full rounded border border-slate-300 bg-white px-2 py-1 text-sm text-slate-900 disabled:cursor-not-allowed disabled:bg-slate-100 disabled:opacity-60 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100 dark:disabled:bg-slate-900/60"
									/>
								</label>
								<label class="text-xs font-semibold text-slate-700 dark:text-slate-300">
									<span>Teleop (s)</span>
									<input
										type="number"
										min="0"
										step="1"
										bind:value={teleopDurationSecondsInput}
										disabled={phase !== 'Idle' || isSavingMatchDurations}
										class="mt-1 w-full rounded border border-slate-300 bg-white px-2 py-1 text-sm text-slate-900 disabled:cursor-not-allowed disabled:bg-slate-100 disabled:opacity-60 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100 dark:disabled:bg-slate-900/60"
									/>
								</label>
							</div>
							<div class="mt-3 flex items-center justify-between gap-3">
								<div class="text-[11px] font-medium text-slate-500 dark:text-slate-400">
									{phase === 'Idle'
										? 'Durations can be changed while the arena is idle.'
										: '🔒 Locked: Return the arena to Idle before changing durations.'}
								</div>
								<button
									type="button"
									onclick={saveMatchDurations}
									disabled={phase !== 'Idle' || isSavingMatchDurations}
									class="brand-secondary-bg cursor-pointer rounded px-3 py-1.5 text-sm font-bold text-white shadow-xs hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-40"
								>
									{isSavingMatchDurations ? 'Saving...' : 'Save Durations'}
								</button>
							</div>
						</div>

						<!-- Alliance Display Order -->
						<div
							class="min-w-[320px] rounded border border-slate-200 bg-slate-50 px-4 py-3 dark:border-slate-700 dark:bg-slate-900/60"
						>
							<div class="text-sm font-bold text-slate-900 dark:text-slate-100">
								Alliance Display Order
							</div>
							<div class="mt-1 text-xs text-slate-600 dark:text-slate-400">
								Configure which alliance is shown on the left side of all audience views.
							</div>
							<div class="mt-3 flex items-center gap-6">
								<label
									class="flex cursor-pointer items-center gap-2 text-sm text-slate-800 dark:text-slate-200"
								>
									<input
										type="radio"
										name="allianceOrder"
										value="redLeft"
										checked={allianceOrderSetting === 'redLeft'}
										onchange={() => fms.setAllianceOrder('redLeft')}
										class="h-4 w-4 cursor-pointer"
									/>
									<span>Red Left, Blue Right</span>
								</label>
								<label
									class="flex cursor-pointer items-center gap-2 text-sm text-slate-800 dark:text-slate-200"
								>
									<input
										type="radio"
										name="allianceOrder"
										value="blueLeft"
										checked={allianceOrderSetting === 'blueLeft'}
										onchange={() => fms.setAllianceOrder('blueLeft')}
										class="h-4 w-4 cursor-pointer"
									/>
									<span>Blue Left, Red Right</span>
								</label>
							</div>
						</div>
					</div>
				</div>

				<!-- Tab 5: Event / The Blue Alliance Import -->
			{:else if activeTab === 'event'}
				<div class="p-3">
					<div class="max-w-lg">
						<div class="text-sm font-bold text-slate-900 dark:text-slate-100">
							Load Teams from The Blue Alliance
						</div>
						<div class="mt-1 text-xs text-slate-600 dark:text-slate-400">
							Enter an event key (e.g. <span class="font-mono font-bold">2026nyny</span>) to fetch
							all teams and avatars at that event into the local database.
						</div>
						<div class="mt-3 flex items-end gap-2">
							<label class="flex-1 text-xs font-semibold text-slate-700 dark:text-slate-300">
								<span>Event Key</span>
								<input
									type="text"
									bind:value={tbaEventKeyInput}
									placeholder="e.g. 2026nyny"
									disabled={isLoadingTeams}
									class="mt-1 w-full rounded border border-slate-300 bg-white px-3 py-2 font-mono text-sm text-slate-900 disabled:opacity-50 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100"
								/>
							</label>
							<button
								type="button"
								onclick={loadTeamsFromTba}
								disabled={isLoadingTeams || !tbaEventKeyInput.trim()}
								class="brand-secondary-bg cursor-pointer rounded px-4 py-2 text-sm font-bold text-white hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
							>
								{isLoadingTeams ? 'Loading…' : 'Load Teams'}
							</button>
						</div>
						{#if isLoadingTeams}
							<div class="mt-2 text-xs text-slate-500 dark:text-slate-400">
								Fetching teams and avatars — this may take 10–30 seconds…
							</div>
						{/if}
						{#if teamLoadResult}
							<div
								class="mt-3 rounded border px-3 py-2 text-xs font-semibold {teamLoadResult.success
									? 'border-emerald-300 bg-emerald-50 text-emerald-800 dark:border-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-300'
									: 'border-rose-300 bg-rose-50 text-rose-700 dark:border-rose-700 dark:bg-rose-950/60 dark:text-rose-300'}"
							>
								{teamLoadResult.message}
							</div>
						{/if}
					</div>
				</div>

				<!-- Tab 6: System Log Console -->
			{:else if activeTab === 'log'}
				<LogConsole />
			{/if}
		</div>
	</main>

	<!-- Modals -->
	<AccessPointModal bind:isOpen={isWpaModalOpen} bind:inputs />
	<OperatorAuthModal bind:isOpen={isAuthModalOpen} />
	<EstopSafetyModal
		isOpen={isEstopSafetyModalOpen}
		onConfirm={async () => {
			isEstopSafetyModalOpen = false;
			await setRequireFieldEstop(false);
		}}
		onCancel={() => {
			isEstopSafetyModalOpen = false;
		}}
	/>

	<!-- Footer -->
	<footer
		class="app-neutral-bg fixed right-0 bottom-0 left-0 border-t border-slate-300 px-3 py-1 text-xs text-slate-600 transition-colors dark:border-slate-700 dark:text-slate-400"
	>
		<div class="relative mx-auto max-w-[1700px]">
			<span
				>{matchState
					? `Loop ${matchState.loopTiming.currentMs.toFixed(2)} ms (30s max ${matchState.loopTiming.maxMs30s.toFixed(2)} ms)`
					: 'Loop — ms'}</span
			>
			<span class="absolute left-1/2 -translate-x-1/2 font-semibold">PossumFMS</span>
			<a href="/audience" class="brand-secondary-text absolute right-0 font-bold hover:opacity-80"
				>Audience Overlay</a
			>
		</div>
	</footer>
</div>
