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

export interface FieldDeviceReplyTimeStats {
	sampleCount: number;
	minMs: number;
	maxMs: number;
	avgMs: number;
	stdDevMs: number;
}

export interface HubDeviceHeartbeat {
	kind: 'Hub';
	receivedUtc: string;
	alliance: string;
	fuelDelta: number;
}

export interface EstopDeviceHeartbeat {
	kind: 'Estop';
	receivedUtc: string;
	field: string;
	station: number;
	astopActivated: boolean;
	estopActivated: boolean;
}

export type FieldDeviceHeartbeat = HubDeviceHeartbeat | EstopDeviceHeartbeat;

export interface FieldDeviceDiagnostics {
	id: number;
	name: string;
	type: string;
	status: string;
	bypassed: boolean;
	lastSeenUtc: string;
	secondsSinceLastSeen: number;
	lastReplyTimeMs: number;
	replyTimeStats: FieldDeviceReplyTimeStats;
	heartbeat: FieldDeviceHeartbeat | null;
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
	currentTeleopPeriod: string; // e.g. "TransitionShift", "Shift1", "Shift2", "Shift3", "Shift4", "EndGame", "NotStarted"
	redBreakdown: AllianceScoreBreakdown;
	blueBreakdown: AllianceScoreBreakdown;
	stationClimbs: StationClimbState[];
	rankingPoints: { red: RankingPointBreakdown; blue: RankingPointBreakdown };
	hubActive: { red: boolean; blue: boolean };
	loopTiming: { currentMs: number; maxMs30s: number };
	accessPoint: { status: string }; // "ACTIVE" | "CONFIGURING" | "ERROR"
	stations: Station[]; // always 6: Red1, Red2, Red3, Blue1, Blue2, Blue3
	fieldDevices: FieldDeviceDiagnostics[];
}

export interface TeamAssignment {
	teamNumber: number;
	wpaKey?: string;
}

export type TowerEndgameLevel = 'None' | 'L1' | 'L2' | 'L3';

export interface AllianceScoreBreakdown {
	autoFuelPoints: number;
	autoTowerPoints: number;
	teleopFuelPoints: number;
	teleopTowerPoints: number;
	fuelCombined: number;
	towerCombined: number;
	total: number;
}

export interface StationClimbState {
	autoClimbed: boolean;
	endgameLevel: TowerEndgameLevel;
}

export interface RankingPointBreakdown {
	energized: boolean;
	supercharged: boolean;
	traversal: boolean;
	winTie: number;
	total: number;
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
	/** Set or clear the bypass flag on a field device */
	bypassFieldDevice(deviceId: number, bypassed: boolean) {
		this.hub?.invoke('BypassFieldDevice', deviceId, bypassed);
	}
	/** Manually push current team assignments to the access point */
	configureAccessPoint() {
		return this.invoke('ConfigureAccessPoint');
	}
	/** Adjust alliance fuel score in Auto or Teleop. Delta may be positive or negative. */
	adjustFuelPoints(alliance: 'Red' | 'Blue', isAuto: boolean, delta: number) {
		return this.invoke('AdjustFuelPoints', alliance, isAuto, delta);
	}
	/** Set whether a station climbed tower in Auto (15 points). */
	setAutoTowerClimb(stationIndex: number, climbed: boolean) {
		return this.invoke('SetAutoTowerClimb', stationIndex, climbed);
	}
	/** Set station endgame climb level: None=0, L1=1, L2=2, L3=3. */
	setEndgameTowerLevel(stationIndex: number, level: TowerEndgameLevel) {
		const numericLevel = level === 'L1' ? 1 : level === 'L2' ? 2 : level === 'L3' ? 3 : 0;
		return this.invoke('SetEndgameTowerLevel', stationIndex, numericLevel);
	}
}

// Singleton — the same instance (and its reactive state) is shared across all
// components that import this module.
export const fms = new FmsConnection();