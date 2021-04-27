# [Trailer](https://youtu.be/fY7Vbl9KKuM) | [Download](https://github.com/billw2012/Bannerlord-Twitch/releases) | [Github](https://github.com/billw2012/Bannerlord-Twitch)

# Bannerlord Twitch (BLT)
This is a modification for [Mount & Blade II: Bannerlord](https://www.taleworlds.com/en/Games/Bannerlord) that adds Twitch integration to the game. This allows events in a Twitch
stream to trigger actions in game, for instance Redemption of Channel Point Rewards, or specific chat messages.

[![Trailer link](https://img.youtube.com/vi/fY7Vbl9KKuM/0.jpg)](https://youtu.be/fY7Vbl9KKuM)

# Features
- **Define Channel Point Rewards**, along with their in game effects, via the configuration file, they will be automatically added to your channel for you, and optionally removed again when the game exits
- **Define Bot Commands** for non Channel Points interactions, with optional limits such as subs only commands 
- Provides an **extensibility framework** allowing for other mods to register action effects and command handlers
- Comes with the **Adopt a Hero module**, allowing viewers to "adopt" an in-game hero, improve them, and perform actions with them in game
- Comes with the **BLT Buffet module**, allowing viewers to perform various actions to spawned agents in game (the player, friendlies, enemies) such as temporary stat changes, attached particle effects, triggering sounds, scaling the character up or down.

## Adopt a Hero
This is the first example action suite that comes with BLT.
Viewers can "adopt" an in-game hero of types that can be specified in the config -- this will give the in-game hero the viewers name, and allow further interactions with them:
- Upgrade battle equipment, civilian equipment, and horse
- Buy skill points, attribute points, focus points
- Summon to the player when they are in missions (including battle, siege, arena, village and town), on the player or enemies side
- Win / lose gold at the end of battles or fights, depending on the outcome 
- Call commands to show their health, gold, last known location, skills, attributes, equipment

## BLT Buffet
This is the second example action suite that comes with BLT.
So far it contains an action/handler called CharacterEffect that is aimed at performing temporary changes to agents that are spawned in missions (i.e. in battle, siege, village, town walkabouts etc., NOT on the map view).  
The changes can only last until the end of the mission at most, and can be limited by time also.  
Possible changes:
- Many agent stats, including things like swing speed, run speed, mount speed, armor, shield skill, courage, etc. [Full list here](https://raw.githubusercontent.com/billw2012/Bannerlord-Twitch/main/BannerlordTwitch/BLTBuffet/CharacterEffectProperties.txt)
- Agent scaling: make giants or dwarves!
- Apply damage or healing over time
- Force the agent to drop their weapons and be unable to pick any up
- Apply damage multiplier to all the agents hits
- Play particles and sounds at the start and end of the effect, [full particle list here](https://raw.githubusercontent.com/billw2012/Bannerlord-Twitch/main/BannerlordTwitch/BLTBuffet/ParticleEffects.txt), [full sound list here](https://raw.githubusercontent.com/billw2012/Bannerlord-Twitch/main/BannerlordTwitch/BLTBuffet/Sounds.txt)
- Attach particles to the agent, their weapon, head, hands, or everywhere on their body

# Instructions

## Installation

### [Installation Guide Video](https://youtu.be/1cBSBwERHjQ)

1. Install [Bannerlord Harmony](https://www.nexusmods.com/mountandblade2bannerlord/mods/2006?tab=files).
   
2. Unzip to the Bannerlords Modules directory (by default at `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules`).
   It should create the `BannerlordTwitch` directory, and the `BannerlordTwitch.dll` should be at `Modules\BannerlordTwitch\bin\Win64_Shipping_Client\BannerlordTwitch.dll`
   ![image](https://user-images.githubusercontent.com/1453936/115397098-9daae880-a1dd-11eb-87c7-0bda9af4c79d.png)
   It should also create the `BLTAdoptAHero` and `BLTBuffet` directories.
   
3. Run the launcher, make sure Harmony loads first and Bannerlord Twitch loads after the game modules and before any BLT extensions:  
   ![image](https://user-images.githubusercontent.com/1453936/116240320-95155d80-a75b-11eb-8920-6e0629ab81b9.png)
   
4. Run the game.
   
5. The mod should popup notifications that it has copied the settings files to their proper locations. When you click okay it will open up `Bannerlord-Twitch-Auth.yaml` for you to edit, to provide authorization. Instructions are in the file itself.
   
6. Once you have edited the config files as you wish you can run the game again.
   
7. During startup watch for notification messages that indicate if the mod initialized successfully and connected to your Twitch channel.
   
9. Once you get to the main menu in game it should be initialized, and the Channel Rewards should have been created automatically.

## Trouble Shooting   
If you have problems you can search for `[BLT]` lines in the `rgl_log` files at `C:\ProgramData\Mount and Blade II Bannerlord\logs`. I added logging for everything so you should see failures and critical errors in here.

If you have problems you can't solve, you can ping me (@billw#7855) in the [TW modding discord](https://discord.gg/hqKcnSNfb6).

# Writing an Extension
You can implement new reward actions and command handlers quite easily:
1. Make another mod that depends on `BannerlordTwitch` (in the `Submodule.xml`, AND reference the dll itself)
2. Implement a new class derived from `IAction` (for rewards), `ICommandHandler` (for bot commands) or `ActionAndHandlerBase` (to make both in one class)
3. Register instances of your derived classes with the `RewardManager`, this can be done easily with a call to:
   ```c#
   RewardManager.RegisterAll(typeof(your module class).Assembly);
   ```

Example:
```c#
// Command handler to allow playing a sound effect in game, passed as the argument to the command itself
public class PlaySfx : ICommandHandler
{
    public void Execute(string args, CommandMessage message, object config)
    {
        // Play the specified sound on the main agent
        if (!string.IsNullOrEmpty(args) && Agent.Main != null)
        {
            Mission.Current.MakeSound(
                SoundEvent.GetEventIdFromString(args),
                Agent.Main.AgentVisuals.GetGlobalFrame().origin,
                false, true, Agent.Main.Index, -1);
        }
    }
    // We don't need any config for this
    public Type HandlerConfigType => null;
}
```

Rewards that have an `Action` that matches the class name of your `IAction` will be passed to your registered instance, along with the `ActionConfig` specified.  
Commands work the same, with `Handler` matching the class name of your `ICommandHandler`, and the `HandlerConfig` section being passed.

Once compiled into a module and installed in the game, it can be used in `Bannerlord-Twitch.yaml` like this:

```yaml
Commands:
- Name: sfx
  Help: Play a sound effect
  Handler: PlaySfx
```

See the `BLTBuffet` and `BLTAdoptAHero` projects for some more examples.


