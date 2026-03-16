import * as signalR from '@microsoft/signalr';

// --- Type definitions matching the FmsHub MatchState broadcast ---

export interface WifiStatus {
	radioLinked: boolean;
	bandwidthMbps: number;
	rxRateMbps: number;
	txRateMbps: number;
	snr: number;
	connectionQuality: number;
}

export interface Station {
	index: number;
	alliance: 'Red' | 'Blue';
	position: number; // 1, 2, or 3
	teamNumber: number;
	dsLinked: boolean;
	robotLinked: boolean;
	radioLinked: boolean;
	rioLinked: boolean;
	battery: number;
	tripTimeMs: number;
	missedPackets: number;
	secondsSinceLastRobotLink: number;
	estop: boolean;
	astop: boolean;
	bypassed: boolean;
	wrongStation: boolean;
	wifi: WifiStatus | null;
}

export interface MatchState {
	phase: string; // e.g. "Idle", "PreMatch", "MatchRunning", "MatchOver"
	matchType: string; // e.g. "Practice", "Qualification", "Playoff"
	matchNumber: number;
	timeRemaining: number; // seconds
	arenaEstop: boolean;
	wasAborted: boolean;
	redScore: number;
	blueScore: number;
	loopTiming: { currentMs: number; maxMs30s: number };
	accessPoint: { status: string }; // "ACTIVE" | "CONFIGURING" | "ERROR"
	stations: Station[]; // always 6: Red1, Red2, Red3, Blue1, Blue2, Blue3
}

export interface TeamAssignment {
	teamNumber: number;
	wpaKey?: string;
}

// Using a class is the recommended Svelte 5 pattern for shared reactive state.
// Class fields declared with $state() are properly tracked as reactive signals
// when accessed from any component that imports this module.
class FmsConnection {
	matchState = $state<MatchState | null>(null);
	connected = $state(false);

	private hub: signalR.HubConnection | null = null;

	// Call this once from a component ($effect or onMount) to start the connection.
	// Multiple calls are safe — the hub is only created once.
	connect() {
		if (this.hub) return;

		this.hub = new signalR.HubConnectionBuilder()
			.withUrl('/fmshub')
			.withAutomaticReconnect()
			.configureLogging(signalR.LogLevel.Warning)
			.build();

		this.hub.on('MatchState', (s: MatchState) => {
			this.matchState = s;
		});

		this.hub.onclose(() => {
			this.connected = false;
		});
		this.hub.onreconnecting(() => {
			this.connected = false;
		});
		this.hub.onreconnected(() => {
			this.connected = true;
			this.hub!.invoke('RequestMatchState');
		});

		this.hub
			.start()
			.then(() => {
				this.connected = true;
				return this.hub!.invoke('RequestMatchState');
			})
			.catch(console.error);
	}

	// --- Hub method wrappers (mirror FmsHub.cs on the backend) ---

	private invoke(methodName: string, ...args: unknown[]): Promise<void> {
		if (!this.hub) return Promise.reject(new Error('FMS is not connected yet.'));
		return this.hub.invoke(methodName, ...args);
	}

	/** Assign a team number (and optional WPA key) to a station (0=Red1 … 5=Blue3) */
	assignTeam(stationIndex: number, teamNumber: number, wpaKey = '') {
		return this.invoke('AssignTeam', stationIndex, teamNumber, wpaKey);
	}
	/** Assign all 6 stations at once in Red1, Red2, Red3, Blue1, Blue2, Blue3 order */
	assignTeams(assignments: TeamAssignment[]) {
		return this.invoke('AssignTeams', assignments);
	}
	/** Move arena to PreMatch phase so teams can connect */
	startPreMatch() {
		this.hub?.invoke('StartPreMatch');
	}
	/** Start the match (must be in PreMatch) */
	startMatch() {
		this.hub?.invoke('StartMatch');
	}
	/** Abort a running match */
	abortMatch() {
		this.hub?.invoke('AbortMatch');
	}
	/** Reset arena back to Idle */
	clearMatch() {
		this.hub?.invoke('ClearMatch');
	}
	/** Kill power to all robots immediately */
	triggerArenaEstop() {
		this.hub?.invoke('TriggerArenaEstop');
	}
	/** Clear the arena-wide e-stop */
	resetArenaEstop() {
		this.hub?.invoke('ResetArenaEstop');
	}
	/** E-stop a single station's robot (0=Red1 … 5=Blue3) */
	estopStation(stationIndex: number) {
		this.hub?.invoke('EstopStation', stationIndex);
	}
	/** A-stop a single station's robot */
	astopStation(stationIndex: number) {
		this.hub?.invoke('AstopStation', stationIndex);
	}
	/** Set or clear the bypass flag on a station */
	bypassStation(stationIndex: number, bypassed: boolean) {
		this.hub?.invoke('BypassStation', stationIndex, bypassed);
	}
	/** Manually push current team assignments to the access point */
	configureAccessPoint() {
		return this.invoke('ConfigureAccessPoint');
	}
}

// Singleton — the same instance (and its reactive state) is shared across all
// components that import this module.
export const fms = new FmsConnection();