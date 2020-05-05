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

        private static WebClient Client = new WebClient();
        private static Process LeagueProcess = null;

        private static string ActivePlayerName = string.Empty;
        private static string ChampionName = string.Empty;
        private static string RawChampionName = string.Empty;

        private static double ClientAttackSpeed = 0.625;
        private static double ChampionAttackCastTime = 0.625;
        private static double ChampionAttackTotalTime = 0.625;
        private static double ChampionAttackSpeedRatio = 0.625;
        private static double ChampionAttackDelayPercent = 0.3;
        private static double ChampionAttackDelayScaling = 1.0;
        private static double WindupBuffer = 0.033;

        public static double GetSecondsPerAttack() => 1 / ClientAttackSpeed;
        public static long GetSecondsPerAttackAsLong() => (long)(GetSecondsPerAttack() * 1000);
        public static double GetWindupDuration() => (((GetSecondsPerAttack() * ChampionAttackDelayPercent) - ChampionAttackCastTime) * ChampionAttackDelayScaling) + ChampionAttackCastTime;
        public static long GetWindupDurationAsLong() => (long)(GetWindupDuration() * 1000) + (long)(WindupBuffer * 1000);

        public static void Main(string[] args)
        {
            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            Console.CursorVisible = false;

            Timer orbWalkTimer = new Timer(33.33);
            orbWalkTimer.Elapsed += AttackSpeedCacheTimer_Elapsed;
            orbWalkTimer.Elapsed += OrbWalkTimer_Elapsed;
            orbWalkTimer.Start();

            CheckLeagueProcess();

            Console.ReadLine();
        }

        private static void InputManager_OnKeyboardEvent(VirtualKeyCode key, KeyState state)
        {
            //TODO: Add keybind checks
        }

        private static DateTime lastInputTime;
        private static long lastWindupDuration = 0;
        private static long lastAttackDuration = 0;
        private static bool activatedChampionTargeting = false;
        private static void OrbWalkTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!HasProcess || IsExiting || GetForegroundWindow() != LeagueProcess.MainWindowHandle)
            {
                InputManager.Keyboard.KeyUp((ushort)DirectInputKeys.DIK_X);
                return;
            }
            else if(!activatedChampionTargeting)
            {
                activatedChampionTargeting = true;
                InputManager.Keyboard.KeyDown((ushort)DirectInputKeys.DIK_X);
            }

            double currentMillis = (e.SignalTime - lastInputTime).TotalMilliseconds;
            if (currentMillis > GetWindupDurationAsLong())
            { 
                if (currentMillis > GetSecondsPerAttackAsLong())
                {
                    InputManager.Keyboard.KeyDown((ushort)DirectInputKeys.DIK_A);
                    InputManager.Mouse.MouseClick(InputManager.Mouse.Buttons.Left);
                    InputManager.Keyboard.KeyUp((ushort)DirectInputKeys.DIK_A);
                    lastInputTime = e.SignalTime;
                    //lastWindupDuration = GetWindupDurationAsLong();
                    //lastAttackDuration = GetSecondsPerAttackAsLong();
                }
                else
                {
                    InputManager.Mouse.MouseClick(InputManager.Mouse.Buttons.Right);
                }
            }
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
            if (HasProcess && !IsExiting && !IsIntializingValues)
            {
                JToken activePlayerToken = JToken.Parse(Client.DownloadString(ActivePlayerEndpoint));
                ActivePlayerName = activePlayerToken["summonerName"].ToString();

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
                            RawChampionName = rawNameArray[rawNameArray.Length - 1];
                        }
                    }

                    try
                    {
                        GetChampionBaseValues(RawChampionName);
                    }
                    catch(Exception ex)
                    {
                        Console.WriteLine($"{ex.Message} {ex.StackTrace}");
                    }
#if DEBUG
                    Console.Title = $"({ActivePlayerName}) {ChampionName}";
                    Console.SetCursorPosition(0, 0);
                    Console.WriteLine($"Attack Speed Ratio: {ChampionAttackSpeedRatio}");
                    Console.WriteLine($"Windup Percent: {ChampionAttackDelayPercent}");
#endif
                    IsIntializingValues = false;
                }
                
                ClientAttackSpeed = activePlayerToken["championStats"]["attackSpeed"].Value<double>();
#if DEBUG
                Console.SetCursorPosition(0, 2);
                Console.WriteLine($"Current AS: {ClientAttackSpeed.ToString().Substring(0, 8)}");
                Console.WriteLine($"Seconds Per Attack: {GetSecondsPerAttack().ToString().Substring(0, 8)}");
                Console.WriteLine($"Windup Duration: {GetWindupDuration().ToString().Substring(0, 8)}s + {WindupBuffer}s delay");
                Console.WriteLine($"Attack Down Time: {(GetSecondsPerAttack() - GetWindupDuration()).ToString().Substring(0, 8)}s");
#endif
            }
        }

        private static void GetChampionBaseValues(string championName)
        {
            string lowerChampionName = championName.ToLower();
            JToken championBinToken = JToken.Parse(Client.DownloadString($"{ChampionStatsEndpoint}{lowerChampionName}/{lowerChampionName}.bin.json"));
            JToken championRootStats = championBinToken[$"Characters/{championName}/CharacterRecords/Root"];
            ChampionAttackSpeedRatio = championRootStats["attackSpeedRatio"].Value<double>(); ;

            JToken championAttackDelayOffsetToken = championRootStats["basicAttack"]["mAttackDelayCastOffsetPercent"];
            JToken championAttackDelayOffsetSpeedRatioToken = championRootStats["basicAttack"]["mAttackDelayCastOffsetPercentAttackSpeedRatio"];

            if(championAttackDelayOffsetSpeedRatioToken?.Value<double?>() != null)
            {
                ChampionAttackDelayScaling = championAttackDelayOffsetSpeedRatioToken.Value<double>();
            }

            if (championAttackDelayOffsetToken?.Value<double?>() == null)
            {
                JToken attackTotalTimeToken = championRootStats["basicAttack"]["mAttackTotalTime"];
                JToken attackCastTimeToken = championRootStats["basicAttack"]["mAttackCastTime"];

                if (attackTotalTimeToken?.Value<double?>() == null && attackCastTimeToken?.Value<double?>() == null)
                {
                    string attackName = championRootStats["basicAttack"]["mAttackName"].ToString();
                    string attackSpell = $"Characters/{attackName.Split(new[] { "BasicAttack" }, StringSplitOptions.RemoveEmptyEntries)[0]}/Spells/{attackName}";
                    JToken attackSpellToken = championBinToken[attackSpell];
                    ChampionAttackDelayPercent += attackSpellToken["mSpell"]["delayCastOffsetPercent"].Value<double>();
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
        }
    }

    public enum AttackCommand
    {
        AttackMove,
        AttackChampOnlyMove,
        Move
    }
}
