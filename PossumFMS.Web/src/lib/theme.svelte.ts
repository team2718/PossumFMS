export type ThemeMode = 'light' | 'dark' | 'system';

class ThemeStore {
	current = $state<ThemeMode>('system');
	isDark = $state<boolean>(false);

	constructor() {
		if (typeof window !== 'undefined') {
			const saved = localStorage.getItem('possum_theme') as ThemeMode | null;
			if (saved === 'light' || saved === 'dark' || saved === 'system') {
				this.current = saved;
			}
			this.applyTheme();

			// Listen for system theme changes
			window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
				if (this.current === 'system') {
					this.applyTheme();
				}
			});
		}
	}

	set(mode: ThemeMode) {
		this.current = mode;
		if (typeof window !== 'undefined') {
			localStorage.setItem('possum_theme', mode);
			this.applyTheme();
		}
	}

	toggle() {
		const next: ThemeMode = this.isDark ? 'light' : 'dark';
		this.set(next);
	}

	private applyTheme() {
		if (typeof window === 'undefined') return;

		let shouldBeDark = false;
		if (this.current === 'dark') {
			shouldBeDark = true;
		} else if (this.current === 'light') {
			shouldBeDark = false;
		} else {
			shouldBeDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
		}

		this.isDark = shouldBeDark;

		// Skip modifying document if on audience page
		if (window.location.pathname.startsWith('/audience')) {
			document.documentElement.classList.remove('dark');
			return;
		}

		if (shouldBeDark) {
			document.documentElement.classList.add('dark');
		} else {
			document.documentElement.classList.remove('dark');
		}
	}
}

export const theme = new ThemeStore();
