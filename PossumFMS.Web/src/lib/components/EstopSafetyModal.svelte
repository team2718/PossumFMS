<script lang="ts">
	let {
		isOpen = false,
		onConfirm,
		onCancel
	} = $props<{
		isOpen: boolean;
		onConfirm: () => void;
		onCancel: () => void;
	}>();
</script>

{#if isOpen}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs transition-opacity"
		role="dialog"
		aria-modal="true"
		aria-labelledby="safety-modal-title"
	>
		<div
			class="w-full max-w-lg rounded-xl border-2 border-rose-500 bg-white p-6 shadow-2xl transition-all dark:border-rose-600 dark:bg-slate-900"
		>
			<div class="flex items-start gap-4">
				<div
					class="flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-rose-100 text-rose-600 dark:bg-rose-950 dark:text-rose-400"
				>
					<svg
						xmlns="http://www.w3.org/2000/svg"
						class="h-6 w-6"
						fill="none"
						viewBox="0 0 24 24"
						stroke="currentColor"
						stroke-width="2"
					>
						<path
							stroke-linecap="round"
							stroke-linejoin="round"
							d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"
						/>
					</svg>
				</div>
				<div class="flex-1">
					<h3 id="safety-modal-title" class="text-lg font-black text-slate-900 dark:text-slate-100">
						Disable Field Hardware E-Stop Requirement?
					</h3>
					<div class="mt-2 space-y-2 text-xs text-slate-600 dark:text-slate-300">
						<p class="font-bold text-rose-700 dark:text-rose-400">
							CAUTION: Disabling this check allows matches to start without a physical hardware
							E-Stop connected to the field.
						</p>
						<div
							class="rounded border border-rose-200 bg-rose-50 p-3 dark:border-rose-900/60 dark:bg-rose-950/40"
						>
							<div class="font-bold text-slate-800 dark:text-slate-200">Safety Repercussions:</div>
							<ul class="mt-1 list-disc space-y-1 pl-4 text-slate-700 dark:text-slate-300">
								<li>
									Physical arena E-Stop push buttons will <strong>not</strong> be required or verified
									before matches start.
								</li>
								<li>
									Software E-Stops and Driver Station aborts will still function, but physical field
									buttons may not respond if disconnected.
								</li>
								<li>
									<strong
										>This setting should strictly be used for offline development, software
										simulations, and UI testing without live robots powered.</strong
									>
								</li>
							</ul>
						</div>
						<p class="text-slate-500 dark:text-slate-400">
							Do you want to proceed and disable this requirement for testing?
						</p>
					</div>
				</div>
			</div>

			<div
				class="mt-6 flex items-center justify-end gap-3 border-t border-slate-200 pt-4 dark:border-slate-800"
			>
				<button
					type="button"
					onclick={onCancel}
					class="cursor-pointer rounded-lg border border-slate-300 bg-white px-4 py-2 text-xs font-bold text-slate-700 shadow-xs hover:bg-slate-100 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700"
				>
					Cancel (Keep Enabled)
				</button>
				<button
					type="button"
					onclick={onConfirm}
					class="cursor-pointer rounded-lg bg-rose-700 px-4 py-2 text-xs font-bold text-white shadow-xs hover:bg-rose-600 active:scale-95"
				>
					Disable Check (Testing Mode)
				</button>
			</div>
		</div>
	</div>
{/if}
