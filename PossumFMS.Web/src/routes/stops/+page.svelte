<script lang="ts">
	import { fms } from '$lib/fms.svelte';
	import Navbar from '$lib/Navbar.svelte';

	$effect(() => {
		fms.connect();
	});

	const matchState = $derived(fms.matchState);
	const phase = $derived(matchState?.phase ?? 'Disconnected');
	const isMatchInProgress = $derived(
		phase === 'Auto' || phase === 'AutoToTeleopTransition' || phase === 'Teleop'
	);

	type StopScope =
		| 'field'
		| 'red'
		| 'blue'
		| 'red1'
		| 'red2'
		| 'red3'
		| 'blue1'
		| 'blue2'
		| 'blue3';

	let scope = $state<StopScope>('field');

	const scopeLabels: Record<StopScope, string> = {
		field: 'Field Wide',
		red: 'Red Alliance',
		blue: 'Blue Alliance',
		red1: 'Red 1',
		red2: 'Red 2',
		red3: 'Red 3',
		blue1: 'Blue 1',
		blue2: 'Blue 2',
		blue3: 'Blue 3'
	};

	function getStationIndices(s: StopScope): number[] {
		switch (s) {
			case 'field':
				return [0, 1, 2, 3, 4, 5];
			case 'red':
				return [0, 1, 2];
			case 'blue':
				return [3, 4, 5];
			case 'red1':
				return [0];
			case 'red2':
				return [1];
			case 'red3':
				return [2];
			case 'blue1':
				return [3];
			case 'blue2':
				return [4];
			case 'blue3':
				return [5];
		}
	}

	function executeEstop() {
		if (scope === 'field') {
			fms.triggerArenaEstop();
		} else {
			for (const idx of getStationIndices(scope)) {
				fms.estopStation(idx);
			}
		}
	}

	function executeAstop() {
		for (const idx of getStationIndices(scope)) {
			fms.astopStation(idx);
		}
	}

	const scopeGroups: { label: string; scopes: StopScope[] }[] = [
		{ label: 'Scope', scopes: ['field', 'red', 'blue'] },
		{ label: 'Red Station', scopes: ['red1', 'red2', 'red3'] },
		{ label: 'Blue Station', scopes: ['blue1', 'blue2', 'blue3'] }
	];

	const scopeButtonClass = (s: StopScope) =>
		scope === s
			? 'bg-slate-800 text-white border-slate-800 font-bold'
			: 'bg-white text-slate-700 border-slate-300 hover:bg-slate-100';
</script>

<div class="app-neutral-bg min-h-screen text-slate-900">
	<Navbar />

	<main class="mx-auto flex max-w-[900px] flex-col gap-4 px-3 py-4">
		<!-- Phase indicator -->
		<div class="flex items-center gap-3 rounded border border-slate-200 bg-white px-4 py-2 shadow-sm">
			<span class="text-sm font-bold text-slate-700">Emergency Stops</span>
			<span
				class="rounded-full border px-3 py-0.5 text-xs font-semibold {isMatchInProgress
					? 'border-emerald-300 bg-emerald-50 text-emerald-800'
					: 'border-slate-300 bg-slate-50 text-slate-600'}"
			>
				{phase}
			</span>
			{#if matchState}
				<span class="text-xs text-slate-500">{matchState.matchType} #{matchState.matchNumber}</span>
			{/if}
		</div>

		<!-- Scope selector -->
		<div class="rounded border border-slate-200 bg-white p-4 shadow-sm">
			<div class="mb-3 text-xs font-bold tracking-wider text-slate-500 uppercase">Target Scope</div>
			<div class="flex flex-col gap-2">
				{#each scopeGroups as group}
					<div class="flex flex-wrap gap-1.5">
						{#each group.scopes as s}
							<button
								onclick={() => (scope = s)}
								class="rounded border px-3 py-1.5 text-sm transition {scopeButtonClass(s)}"
							>
								{scopeLabels[s]}
							</button>
						{/each}
					</div>
				{/each}
			</div>
			<div class="mt-3 text-xs font-semibold text-slate-600">
				Current target: <span class="text-slate-900">{scopeLabels[scope]}</span>
			</div>
		</div>

		<!-- Large action buttons -->
		<div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
			<!-- Match Abort -->
			<button
				onclick={() => fms.abortMatch()}
				disabled={!isMatchInProgress}
				class="flex min-h-[12rem] flex-col items-center justify-center gap-3 rounded-xl border-2 border-orange-700 bg-orange-600 px-4 py-6 text-white shadow-lg transition active:translate-y-px active:shadow-md disabled:cursor-not-allowed disabled:opacity-40 hover:bg-orange-500"
			>
				<svg
					xmlns="http://www.w3.org/2000/svg"
					viewBox="0 0 24 24"
					fill="currentColor"
					class="h-12 w-12"
				>
					<path
						fill-rule="evenodd"
						d="M12 2.25c-5.385 0-9.75 4.365-9.75 9.75s4.365 9.75 9.75 9.75 9.75-4.365 9.75-9.75S17.385 2.25 12 2.25Zm3 10.5a.75.75 0 0 0 0-1.5H9a.75.75 0 0 0 0 1.5h6Z"
						clip-rule="evenodd"
					/>
				</svg>
				<div class="text-center">
					<div class="text-xl font-black tracking-wide">MATCH ABORT</div>
					<div class="mt-1 text-xs font-semibold opacity-80">Ends the match immediately</div>
				</div>
			</button>

			<!-- A-Stop -->
			<button
				onclick={executeAstop}
				class="flex min-h-[12rem] flex-col items-center justify-center gap-3 rounded-xl border-2 border-amber-700 bg-amber-500 px-4 py-6 text-white shadow-lg transition active:translate-y-px active:shadow-md hover:bg-amber-400"
			>
				<svg
					xmlns="http://www.w3.org/2000/svg"
					viewBox="0 0 24 24"
					fill="currentColor"
					class="h-12 w-12"
				>
					<path
						fill-rule="evenodd"
						d="M9.401 3.003c1.155-2 4.043-2 5.197 0l7.355 12.748c1.154 2-.29 4.5-2.599 4.5H4.645c-2.309 0-3.752-2.5-2.598-4.5L9.4 3.003ZM12 8.25a.75.75 0 0 1 .75.75v3.75a.75.75 0 0 1-1.5 0V9a.75.75 0 0 1 .75-.75Zm0 8.25a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5Z"
						clip-rule="evenodd"
					/>
				</svg>
				<div class="text-center">
					<div class="text-xl font-black tracking-wide">A-STOP</div>
					<div class="mt-1 text-xs font-semibold opacity-80">{scopeLabels[scope]}</div>
				</div>
			</button>

			<!-- E-Stop -->
			<button
				onclick={executeEstop}
				class="flex min-h-[12rem] flex-col items-center justify-center gap-3 rounded-xl border-2 border-rose-900 bg-rose-700 px-4 py-6 text-white shadow-lg transition active:translate-y-px active:shadow-md hover:bg-rose-600"
			>
				<svg
					xmlns="http://www.w3.org/2000/svg"
					viewBox="0 0 24 24"
					fill="currentColor"
					class="h-12 w-12"
				>
					<path
						fill-rule="evenodd"
						d="M2.25 12c0-5.385 4.365-9.75 9.75-9.75s9.75 4.365 9.75 9.75-4.365 9.75-9.75 9.75S2.25 17.385 2.25 12Zm6-2.438c0-.724.588-1.312 1.313-1.312h4.874c.725 0 1.313.588 1.313 1.313v4.874c0 .725-.588 1.313-1.313 1.313H9.564a1.312 1.312 0 0 1-1.313-1.313V9.564Z"
						clip-rule="evenodd"
					/>
				</svg>
				<div class="text-center">
					<div class="text-xl font-black tracking-wide">E-STOP</div>
					<div class="mt-1 text-xs font-semibold opacity-80">{scopeLabels[scope]}</div>
				</div>
			</button>
		</div>

		<!-- Arena E-Stop reset (shown when active) -->
		{#if matchState?.arenaEstop}
			<div class="flex items-center justify-between gap-3 rounded-xl border-2 border-rose-300 bg-rose-50 px-4 py-3">
				<span class="font-bold text-rose-700">Arena E-Stop is ACTIVE</span>
				<button
					onclick={() => fms.resetArenaEstop()}
					class="rounded bg-yellow-500 px-4 py-2 font-bold text-slate-900 hover:bg-yellow-400"
				>
					Reset E-Stop
				</button>
			</div>
		{/if}
	</main>
</div>
