<h1 align="center">
    Odd AutoWalker
</h1>
<h4 align="center">
    Get a competitive edge and save your wrists in League of Legends. This tool is designed to orb walk optimally by calculating the amount of time it takes to complete an auto attack.
</h4>

<p align="center">
    <img src="https://odd.dev/videos/league_kogmaw_autowalker.gif">
</p>

---

<h2>
    How It Works
</h2>

---

Utilizes the [League of Legends Live Client API](https://developer.riotgames.com/docs/lol#game-client-api_live-client-data-api) to get your in-game stats to calculate the appropriate times to issue moves and attacks.
Uses [LowLevelInput.Net](https://github.com/michel-pi/LowLevelInput.Net) to be able to capture the user's input to know when to start issuing actions. The actions are hardware emulated using `SendInput` found in [InputSimulator](https://github.com/approved/OddAutoWalker/blob/master/OddAutoWalker/InputSimulator.cs)

<br>

<h2>
    Using This Program
</h2>

<h5> While this program is usable, it is intended to be used as reference for both a better implementation and your own project.
</h5>

<h5>
    If you don't want to mess with the program yourself, you must have your "Player Attack Move" bound to 'A'. <br>
    This setting can be found in the in-game settings at Settings->Hotkeys->Player Movement.
</h5>

---

Steps:

1. Launch OddAutoWalker.exe
2. Wait until you're in game
3. Press and hold 'C' to activate the auto walker
