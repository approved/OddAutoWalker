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

        private static readonly Timer OrbWalkTimer = new Timer(100d/3d);

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
        private static double WindupBuffer = 1d / 30d;

#if DEBUG
        private static int TimerCallbackCounter = 0;
#endif

        public static double GetSecondsPerAttack() => 1 / ClientAttackSpeed;
        public static long GetSecondsPerAttackAsLong() => (long)(GetSecondsPerAttack() * 1000);
        public static double GetWindupDuration() => (((GetSecondsPerAttack() * ChampionAttackDelayPercent) - ChampionAttackCastTime) * ChampionAttackDelayScaling) + ChampionAttackCastTime;
        public static double GetBufferedWindupDuration() => (GetWindupDuration() * 1000) + (WindupBuffer * 1000);
        public static long GetWindupDurationAsLong() => (long)(GetWindupDuration() * 1000) + (long)(WindupBuffer * 1000);

        public static void Main(string[] args)
        {
            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            Console.CursorVisible = false;

            InputManager.Initialize();
            InputManager.OnKeyboardEvent += InputManager_OnKeyboardEvent;
            InputManager.OnMouseEvent += InputManager_OnMouseEvent;

            OrbWalkTimer.Elapsed += OrbWalkTimer_Elapsed;
#if DEBUG
            Timer callbackTimer = new Timer(16.66);
            callbackTimer.Elapsed += Timer_CallbackLog;
#endif

            Timer attackSpeedCacheTimer = new Timer(33.33);
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
                switch(state)
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

        private static DateTime lastInputTime;
        private static DateTime lastMoveTime;
        private static long lastWindupDuration = 0;
        private static long lastAttackDuration = 0;
        private static bool activatedChampionTargeting = false;

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
                if (activatedChampionTargeting)
                {
                    InputSimulator.Keyboard.KeyUp((ushort)DirectInputKeys.DIK_X);
                }

                return;
            }
            
            if(!activatedChampionTargeting)
            {
                activatedChampionTargeting = true;
                InputSimulator.Keyboard.KeyDown((ushort)DirectInputKeys.DIK_X);
            }

            double currentMillis = (e.SignalTime - lastInputTime).TotalMilliseconds;
            if (currentMillis > lastWindupDuration)
            {
                if (currentMillis - (WindupBuffer * 1000) > lastAttackDuration)
                {
                    lastInputTime = e.SignalTime;
                    lastWindupDuration = GetWindupDurationAsLong();
                    lastAttackDuration = GetSecondsPerAttackAsLong();
                    InputSimulator.Keyboard.KeyDown((ushort)DirectInputKeys.DIK_A);
                    InputSimulator.Mouse.MouseClick(InputSimulator.Mouse.Buttons.Left);
                    InputSimulator.Keyboard.KeyUp((ushort)DirectInputKeys.DIK_A);
                }
                else if ((e.SignalTime - lastMoveTime).TotalMilliseconds >= 100)
                {
                    lastMoveTime = e.SignalTime;
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

                ActivePlayerName = activePlayerToken?["summonerName"].ToString();

                if (string.IsNullOrEmpty(ChampionName))
                {
                    IsIntializingValues = true;
                    JToken playerListToken = JToken.Parse(Client.DownloadString(PlayerListEndpoint));
                    foreach(JToken token in playerListToken)
                    {
                        if(token["summonerName"].ToString().Equals(ActivePlayerName))
                        {
                            ChampionName = token["championName"].ToString();
                            string[] rawNameArray = token["rawChampionName"].ToString().Split('_', StringSplitOptions.RemoveEmptyEntries);
                            RawChampionName = rawNameArray[^1];
                        }
                    }

                    if(!GetChampionBaseValues(RawChampionName))
                    {
                        IsIntializingValues = false;
                        IsUpdatingAttackValues = false;
                        return;
                    }
#if DEBUG
                    Console.Title = $"({ActivePlayerName}) {ChampionName}";
                    Console.SetCursorPosition(0, 0);
                    Console.WriteLine($"{owStopWatch.ElapsedMilliseconds}\n" +
                        $"Attack Speed Ratio: {ChampionAttackSpeedRatio}\n" +
                        $"Windup Percent: {ChampionAttackDelayPercent}\n" +
                        $"Current AS: {ClientAttackSpeed:#.######}\n+" +
                        $"Seconds Per Attack: {GetSecondsPerAttack():#.######}\n" +
                        $"Windup Duration: {GetWindupDuration():#.######}s + {WindupBuffer}s delay\n" +
                        $"Attack Down Time: {(GetSecondsPerAttack() - GetWindupDuration()):#.######}s");
#endif
                    IsIntializingValues = false;
                }
                
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

            if(championAttackDelayOffsetSpeedRatioToken?.Value<double?>() != null)
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
