using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using Funnies.Commands;

namespace Funnies.Modules;

public class Invisible
{

    private static List<CEntityInstance> _entities = [];

    public static void OnPlayerTransmit(CCheckTransmitInfo info, CCSPlayerController player)
    {
        // TODO: Should store these but dont know a good way :/
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First();

        foreach (var entity in _entities)
        {
            if (!Globals.InvisiblePlayers.ContainsKey(player) && player.Team != CsTeam.Spectator)
                info.TransmitEntities.Remove(entity);
        }

        if (gameRules.GameRules!.WarmupPeriod) return;

        var c4s = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4");

        if (c4s.Any())
        {
            var c4 = c4s.First();
            if (player!.Team != CsTeam.Terrorist && !gameRules.GameRules!.BombPlanted && !c4.IsPlantingViaUse  && !gameRules.GameRules!.BombDropped)
                info.TransmitEntities.Remove(c4);
            else
                info.TransmitEntities.Add(c4);
        }
    }

    public static void OnTick()
    {
        _entities.Clear();
        
        foreach (var invis in Globals.InvisiblePlayers)
        {
            if (!Util.IsPlayerValid(invis.Key)) continue;

            var currentWeapon = invis.Key.PlayerPawn.Value.WeaponServices.ActiveWeapon.Get().As<CCSWeaponBase>();
            if (currentWeapon.IsValid)
            {
                if (currentWeapon.InReload && !invis.Value.HackyReload)
                {
                    var data = Globals.InvisiblePlayers[invis.Key];
                    data.HackyReload = true;
                    Globals.InvisiblePlayers[invis.Key] = data;
                    SetPlayerInvisibleFor(invis.Key, currentWeapon.VData.DisallowAttackAfterReloadStartDuration);
                }
            }
            
            var alpha = 255f;

            var half = Server.CurrentTime + ((invis.Value.StartTime - Server.CurrentTime) / 2);
            if (half < Server.CurrentTime)
                alpha = invis.Value.EndTime < Server.CurrentTime ? 0 : Util.Map(Server.CurrentTime, half, invis.Value.EndTime, 255, 0);

            var progress = (int)Util.Map(alpha, 0, 255, 0, 20);
            var pawn = invis.Key.PlayerPawn.Value;

            if (alpha == 0)
            {
                pawn!.EntitySpottedState.Spotted = false;
                pawn!.EntitySpottedState.SpottedByMask[0] = 0;
                _entities.Add(pawn);
                var data = Globals.InvisiblePlayers[invis.Key];
                data.HackyReload = false;
                Globals.InvisiblePlayers[invis.Key] = data;
            }
            else
                _entities.Remove(pawn);

            invis.Key.PrintToCenterHtml(string.Concat(Enumerable.Repeat("&#9608;", progress)) + string.Concat(Enumerable.Repeat("&#9617;", 20 - progress)));

            pawn!.Render = Color.FromArgb((int)alpha, pawn.Render);
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");

            pawn.ShadowStrength = alpha < 128f ? 1.0f : 0.0f;
            Utilities.SetStateChanged(pawn!, "CBaseModelEntity", "m_flShadowStrength");

            foreach (var weapon in pawn.WeaponServices!.MyWeapons)
            {
                weapon.Value!.ShadowStrength = alpha < 128f ? 1.0f : 0.0f;
                Utilities.SetStateChanged(weapon.Value!, "CBaseModelEntity", "m_flShadowStrength");

                if (alpha < 128f)
                {
                    weapon.Value!.Render = Color.FromArgb((int)alpha, pawn.Render);
                    Utilities.SetStateChanged(weapon.Value!, "CBaseModelEntity", "m_clrRender");
                    _entities.Add(weapon.Value!);
                }
            }
        }
    }

    public static HookResult OnPlayerSound(EventPlayerSound @event, GameEventInfo info)
    {
        SetPlayerInvisibleFor(@event.Userid, @event.Duration * 2);

        return HookResult.Continue;
    }

    public static HookResult OnPlayerShoot(EventBulletImpact @event, GameEventInfo info)
    {
        SetPlayerInvisibleFor(@event.Userid, 0.5f);

        return HookResult.Continue;
    }

    public static HookResult OnPlayerStartPlant(EventBombBeginplant @event, GameEventInfo info)
    {
        SetPlayerInvisibleFor(@event.Userid, 1f);

        return HookResult.Continue;
    }

    public static HookResult OnPlayerStartDefuse(EventBombBegindefuse @event, GameEventInfo info)
    {
        SetPlayerInvisibleFor(@event.Userid, 1f);

        return HookResult.Continue;
    }

    /*public static HookResult OnPlayerReload(EventWeaponReload @event, GameEventInfo info)
    {
        var data = Globals.InvisiblePlayers[@event.Userid];
        data.HackyReload = true;
        SetPlayerInvisibleFor(@event.Userid, 1.5f);

        return HookResult.Continue;
    }*/

    public static HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        SetPlayerInvisibleFor(@event.Userid, 0.5f);

        return HookResult.Continue;
    }

    private static void SetPlayerInvisibleFor(CCSPlayerController player, float time)
    {
        if (!Util.IsPlayerValid(player)) return;
        if (!Globals.InvisiblePlayers.TryGetValue(player, out var data)) return;

        data.StartTime = Server.CurrentTime;
        data.EndTime = Server.CurrentTime + time;

        Globals.InvisiblePlayers[player] = data;
    }

    public static void Setup()
    {
        Globals.Plugin.RegisterEventHandler<EventBombBeginplant>(OnPlayerStartPlant);
        // EventPlayerShoot doesnt work so we use EventBulletImpact
        Globals.Plugin.RegisterEventHandler<EventBulletImpact>(OnPlayerShoot);
        Globals.Plugin.RegisterEventHandler<EventPlayerSound>(OnPlayerSound);
        Globals.Plugin.RegisterEventHandler<EventBombBegindefuse>(OnPlayerStartDefuse);
        // Globals.Plugin.RegisterEventHandler<EventWeaponReload>(OnPlayerReload);
        Globals.Plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);

        Globals.Plugin.AddCommand("css_invisible", "Makes a player invisible", CommandInvisible.OnInvisibleCommand);
        Globals.Plugin.AddCommand("css_invis", "Makes a player invisible", CommandInvisible.OnInvisibleCommand);
    }

    public static void Cleanup()
    {
        _entities.Clear();

        foreach (var player in Util.GetValidPlayers())
        {
            var pawn = player.PlayerPawn.Value;

            pawn!.Render = Color.FromArgb(255, pawn.Render);
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
            pawn!.ShadowStrength = 1.0f;
            Utilities.SetStateChanged(pawn!, "CBaseModelEntity", "m_flShadowStrength");

            foreach (var weapon in pawn.WeaponServices!.MyWeapons)
            {
                weapon.Value!.ShadowStrength = 1.0f;
                Utilities.SetStateChanged(weapon.Value!, "CBaseModelEntity", "m_flShadowStrength");
            }
        }

        Globals.InvisiblePlayers.Clear();
    }
}