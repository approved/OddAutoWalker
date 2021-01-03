using LowLevelInput.Hooks;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Timers;

namespace OddAutoWalker
{
    public class Program
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private const string ActivePlayerEndpoint = @"https://127.0.0.1:2999/liveclientdata/activeplayer";
        private const string PlayerListEndpoint = @"https://127.0.0.1:2999/liveclientdata/playerlist";
        private const string ChampionStatsEndpoint = @"https://raw.communitydragon.org/latest/game/data/characters/";

        private static bool HasProcess = false;
        private static bool IsExiting = false;
        private static bool IsIntializingValues = false;
        private static bool IsUpdatingAttackValues = false;

        private static readonly WebClient Client = new WebClient();
        private static readonly InputManager InputManager = new InputManager();
        private static Process LeagueProcess = null;

        private static readonly Timer OrbWalkTimer = new Timer(100d / 3d);

        private static bool OrbWalkerTimerActive = false;

        private static string ActivePlayerName = string.Empty;
        private static string ChampionName = string.Empty;
        private static string RawChampionName = string.Empty;

        private static double ClientAttackSpeed = 0.625;
        private static double ChampionAttackCastTime = 0.625;
        private static double ChampionAttackTotalTime = 0.625;
        private static double ChampionAttackSpeedRatio = 0.625;
        private static double ChampionAttackDelayPercent = 0.3;
        private static double ChampionAttackDelayScaling = 1.0;

        /// <summary>
        /// This is a buffer to prevent you from accidentally canceling your auto-attack too soon, as a result of fps, ping, or otherwise.
        /// </summary>
        private static readonly double WindupBuffer = 1d / 15d;

        // If we're trying to input faster than this, don't
        private static readonly double MinInputDelay = 1d / 30d;

        // This is honestly just semi-random because we need an interval to run the timer at
        private static readonly double OrderTickRate = 1d / 30d;

#if DEBUG
        private static int TimerCallbackCounter = 0;
#endif

        // These are all in seconds
        public static double GetSecondsPerAttack() => 1 / ClientAttackSpeed;
        public static double GetWindupDuration() => (((GetSecondsPerAttack() * ChampionAttackDelayPercent) - ChampionAttackCastTime) * ChampionAttackDelayScaling) + ChampionAttackCastTime;
        public static double GetBufferedWindupDuration() => GetWindupDuration() + WindupBuffer;

        public static void Main(string[] args)
        {
            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            Client.Proxy = null;

            Console.Clear();
            Console.CursorVisible = false;

            InputManager.Initialize();
            InputManager.OnKeyboardEvent += InputManager_OnKeyboardEvent;
            InputManager.OnMouseEvent += InputManager_OnMouseEvent;

            OrbWalkTimer.Elapsed += OrbWalkTimer_Elapsed;
#if DEBUG
            Timer callbackTimer = new Timer(16.66);
            callbackTimer.Elapsed += Timer_CallbackLog;
#endif

            Timer attackSpeedCacheTimer = new Timer(OrderTickRate);
            attackSpeedCacheTimer.Elapsed += AttackSpeedCacheTimer_Elapsed;

            attackSpeedCacheTimer.Start();

            CheckLeagueProcess();

            Console.ReadLine();
        }

#if DEBUG
        private static void Timer_CallbackLog(object sender, ElapsedEventArgs e)
        {
            if (TimerCallbackCounter > 1 || TimerCallbackCounter < 0)
            {
                Console.Clear();
                Console.WriteLine("Timer Error Detected");
                throw new Exception("Timers must not run simultaneously");
            }
        }
#endif

        private static void InputManager_OnMouseEvent(VirtualKeyCode key, KeyState state, int x, int y)
        {
        }

        private static void InputManager_OnKeyboardEvent(VirtualKeyCode key, KeyState state)
        {
            if (key == VirtualKeyCode.C)
            {
                switch (state)
                {
                    case KeyState.Down when !OrbWalkerTimerActive:
                        OrbWalkerTimerActive = true;
                        OrbWalkTimer.Start();
                        break;

                    case KeyState.Up when OrbWalkerTimerActive:
                        OrbWalkerTimerActive = false;
                        OrbWalkTimer.Stop();
                        break;
                }
            }
        }

        // When these DateTime instances are in the past, the action they gate can be taken
        private static DateTime nextInput = default;
        private static DateTime nextMove = default;
        private static DateTime nextAttack = default;

        private static readonly Stopwatch owStopWatch = new Stopwatch();

        private static void OrbWalkTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
#if DEBUG
            owStopWatch.Start();
            TimerCallbackCounter++;
#endif
            if (!HasProcess || IsExiting || GetForegroundWindow() != LeagueProcess.MainWindowHandle)
            {
#if DEBUG
                TimerCallbackCounter--;
#endif

                return;
            }

            // Store time at timer tick start into a variable for readability
            var time = e.SignalTime;

            // Make sure we can send input without being dropped
            // This is used for gating movement orders when waiting for an attack to be prepared
            // This is not needed if this function is not ran frequently enough for it to matter
            // If it isn't, you might end up with this timer and this function's timer being out of sync
            //   resulting in a (worst-case) OrderTickRate + MinInputDelay delay
            // It is currently disabled due to this, enable it if you want/need to
            if (true || nextInput < time)
            {
                // If we can attack, do so
                if (nextAttack < time)
                {
                    // Store current time + input delay so we're aware when we can move next
                    nextInput = time.AddSeconds(MinInputDelay);

                    // Send attack input
                    InputSimulator.Keyboard.KeyDown((ushort)DirectInputKeys.DIK_A);
                    InputSimulator.Mouse.MouseClick(InputSimulator.Mouse.Buttons.Left);
                    InputSimulator.Keyboard.KeyUp((ushort)DirectInputKeys.DIK_A);

                    // We've sent input now, so we're re-fetching time as I have no idea how long input takes
                    // I'm assuming it's negligable, but why not
                    // Please check what the actual difference is if you consider keeping this lol
                    var attackTime = DateTime.Now;

                    // Store timings for when to next attack / move
                    nextMove = attackTime.AddSeconds(GetBufferedWindupDuration());
                    nextAttack = attackTime.AddSeconds(GetSecondsPerAttack());
                }
                // If we can't attack but we can move, do so
                else if (nextMove < time)
                {
                    // Store current time + input delay so we're aware when we can attack / move next
                    nextInput = time.AddSeconds(MinInputDelay);

                    // Send move input
                    InputSimulator.Mouse.MouseClick(InputSimulator.Mouse.Buttons.Right);
                }
            }
#if DEBUG
            TimerCallbackCounter--;
            owStopWatch.Reset();
#endif
        }

        private static void CheckLeagueProcess()
        {
            while (LeagueProcess is null || !HasProcess)
            {
                LeagueProcess = Process.GetProcessesByName("League of Legends").FirstOrDefault();
                if (LeagueProcess is null || LeagueProcess.HasExited)
                {
                    continue;
                }
                HasProcess = true;
                LeagueProcess.EnableRaisingEvents = true;
                LeagueProcess.Exited += LeagueProcess_Exited;
            }
        }

        private static void LeagueProcess_Exited(object sender, EventArgs e)
        {
            HasProcess = false;
            LeagueProcess = null;
            //Console.Clear();
            Console.WriteLine("League Process Exited");
            CheckLeagueProcess();
        }

        private static void AttackSpeedCacheTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (HasProcess && !IsExiting && !IsIntializingValues && !IsUpdatingAttackValues)
            {
                IsUpdatingAttackValues = true;

                JToken activePlayerToken = null;
                try
                {
                    activePlayerToken = JToken.Parse(Client.DownloadString(ActivePlayerEndpoint));
                }
                catch
                {
                    IsUpdatingAttackValues = false;
                    return;
                }

                if (string.IsNullOrEmpty(ChampionName))
                {
                    ActivePlayerName = activePlayerToken?["summonerName"].ToString();
                    IsIntializingValues = true;
                    JToken playerListToken = JToken.Parse(Client.DownloadString(PlayerListEndpoint));
                    foreach (JToken token in playerListToken)
                    {
                        if (token["summonerName"].ToString().Equals(ActivePlayerName))
                        {
                            ChampionName = token["championName"].ToString();
                            string[] rawNameArray = token["rawChampionName"].ToString().Split('_', StringSplitOptions.RemoveEmptyEntries);
                            RawChampionName = rawNameArray[^1];
                        }
                    }

                    if (!GetChampionBaseValues(RawChampionName))
                    {
                        IsIntializingValues = false;
                        IsUpdatingAttackValues = false;
                        return;
                    }

#if DEBUG
                    Console.Title = $"({ActivePlayerName}) {ChampionName}";
#endif

                    IsIntializingValues = false;
                }

#if DEBUG
                Console.SetCursorPosition(0, 0);
                Console.WriteLine($"{owStopWatch.ElapsedMilliseconds}\n" +
                    $"Attack Speed Ratio: {ChampionAttackSpeedRatio}\n" +
                    $"Windup Percent: {ChampionAttackDelayPercent}\n" +
                    $"Current AS: {ClientAttackSpeed:0.00####}\n" +
                    $"Seconds Per Attack: {GetSecondsPerAttack():0.00####}\n" +
                    $"Windup Duration: {GetWindupDuration():0.00####}s + {WindupBuffer}s delay\n" +
                    $"Attack Down Time: {(GetSecondsPerAttack() - GetWindupDuration()):0.00####}s");
#endif

                ClientAttackSpeed = activePlayerToken["championStats"]["attackSpeed"].Value<double>();
                IsUpdatingAttackValues = false;
            }
        }

        private static bool GetChampionBaseValues(string championName)
        {
            string lowerChampionName = championName.ToLower();
            JToken championBinToken = null;
            try
            {
                championBinToken = JToken.Parse(Client.DownloadString($"{ChampionStatsEndpoint}{lowerChampionName}/{lowerChampionName}.bin.json"));
            }
            catch
            {
                return false;
            }
            JToken championRootStats = championBinToken[$"Characters/{championName}/CharacterRecords/Root"];
            ChampionAttackSpeedRatio = championRootStats["attackSpeedRatio"].Value<double>(); ;

            JToken championBasicAttackInfoToken = championRootStats["basicAttack"];
            JToken championAttackDelayOffsetToken = championBasicAttackInfoToken["mAttackDelayCastOffsetPercent"];
            JToken championAttackDelayOffsetSpeedRatioToken = championBasicAttackInfoToken["mAttackDelayCastOffsetPercentAttackSpeedRatio"];

            if (championAttackDelayOffsetSpeedRatioToken?.Value<double?>() != null)
            {
                ChampionAttackDelayScaling = championAttackDelayOffsetSpeedRatioToken.Value<double>();
            }

            if (championAttackDelayOffsetToken?.Value<double?>() == null)
            {
                JToken attackTotalTimeToken = championBasicAttackInfoToken["mAttackTotalTime"];
                JToken attackCastTimeToken = championBasicAttackInfoToken["mAttackCastTime"];

                if (attackTotalTimeToken?.Value<double?>() == null && attackCastTimeToken?.Value<double?>() == null)
                {
                    string attackName = championBasicAttackInfoToken["mAttackName"].ToString();
                    string attackSpell = $"Characters/{attackName.Split(new[] { "BasicAttack" }, StringSplitOptions.RemoveEmptyEntries)[0]}/Spells/{attackName}";
                    ChampionAttackDelayPercent += championBinToken[attackSpell]["mSpell"]["delayCastOffsetPercent"].Value<double>();
                }
                else
                {
                    ChampionAttackTotalTime = attackTotalTimeToken.Value<double>();
                    ChampionAttackCastTime = attackCastTimeToken.Value<double>(); ;

                    ChampionAttackDelayPercent = ChampionAttackCastTime / ChampionAttackTotalTime;
                }
            }
            else
            {
                ChampionAttackDelayPercent += championAttackDelayOffsetToken.Value<double>(); ;
            }

            return true;
        }
    }
}
