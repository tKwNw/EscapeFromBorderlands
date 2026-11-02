using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class ChestRigMedical
{
    internal sealed class Use
    {
        internal ChestRigRuntime.State State = null!;
        internal Item Item = null!;
        internal EBodyPart Part;
        internal float Resource, Hp;
        internal readonly List<object> Bleeds = new();
        internal readonly List<EBodyPart> Blacked = new();
        internal float SurgeryPenalty, HealthCost, PaidHealthCost;
    }
    internal static readonly Dictionary<object, Use> Uses = new();
    internal static void Reset() => Uses.Clear();
    internal static void Unequip(ChestRigRuntime.State state)
    { foreach(var effect in Uses.Where(kv=>kv.Value.State==state).Select(kv=>kv.Key).ToArray())Uses.Remove(effect);foreach(var key in state.Ledger.Until.Keys.Where(k=>k.StartsWith(state.Key+":",StringComparison.Ordinal)).ToArray()) state.Ledger.Until.Remove(key); }
    internal static void Begin(object effect)
    {
        var player=BackpackRuntime.Actor(Native.Get(effect,"HealthController"));var state=ChestRigRuntime.Get(player);
        if(state is null||Native.Get(effect,"MedItem") is not Item item)return;
        var part=Native.Get(effect,"BodyPart") is EBodyPart body?body:EBodyPart.Chest;
        var u=new Use{State=state,Item=item,Part=part,Resource=Native.Component<MedKitComponent>(item)?.HpResource??0f,Hp=player!.ActiveHealthController.GetBodyPartHealth(part,false).Current};
        u.Bleeds.AddRange(player.ActiveHealthController.GetAllEffects().Where(e=>e.Existing&&e.BodyPart==part&&e.GetType().Name is "LightBleeding" or "HeavyBleeding").Cast<object>());
        foreach(var p in Repairable)if(player.ActiveHealthController.IsBodyPartDestroyed(p))u.Blacked.Add(p);
        Uses[effect]=u;
    }
    internal static readonly EBodyPart[] Repairable={EBodyPart.LeftArm,EBodyPart.RightArm,EBodyPart.LeftLeg,EBodyPart.RightLeg,EBodyPart.Stomach};
    internal static void End(object effect)
    {
        if(!Uses.TryGetValue(effect,out var u))return;Uses.Remove(effect);
        if(Native.Get(effect,"_interrupted") is true||ChestRigRuntime.Get(u.State.Player)!=u.State)return;
        var s=u.State;var p=s.Player;var h=p.ActiveHealthController;
        var remainingBleeds=h.GetAllEffects().Where(e=>e.Existing).Cast<object>().ToArray();
        if(s.Key=="AR03-1"&&u.Bleeds.Any(e=>!remainingBleeds.Contains(e)))s.Arm("triageHeal",10f);
        if(s.Key=="AR06-1"&&s.Ready&&Native.Component<FoodDrinkComponent>(u.Item) is null){ChestRigRuntime.RestoreArms(p,.25f);s.DrawCharge=1.6f;s.Cooldown(20f);}
        if(s.Key=="AR23-1"&&u.Blacked.Contains(u.Part)&&!h.IsBodyPartDestroyed(u.Part))
        {
            foreach(var part in Repairable.Where(b=>b!=u.Part&&h.IsBodyPartDestroyed(b)))h.RestoreBodyPart(part,u.SurgeryPenalty>0f?u.SurgeryPenalty:NativeSurgeryPenalty(u.Item));
            EffectStatusModel.Notify(p.ProfileId,"rig:"+s.Key,s.Roll.Definition.ShortName,Time.time,"surgery completed");
        }
    }
    internal static float MedicalResource(float proposed,object effect)
    {
        if(!Uses.TryGetValue(effect,out var u)||u.State.Key!="AR09-2"||Native.Component<MedKitComponent>(u.Item) is not { } med||med.HpResourceRate<=0)return proposed;
        var spent=Math.Max(0f,med.HpResource-proposed);u.HealthCost+=spent;
        var wanted=Math.Max(1f,u.HealthCost*.75f);var pay=Math.Min(med.HpResource,Math.Max(0f,wanted-u.PaidHealthCost));
        u.PaidHealthCost+=pay;return med.HpResource-pay;
    }
    private static float NativeSurgeryPenalty(Item item)
    {
        var effects=Native.Component<HealthEffectsComponent>(item)?.DamageEffects;
        if(effects is not null&&effects.FirstOrDefault(x=>x.Key.ToString()=="DestroyedPart").Value is { } surgery)
        {var min=Native.Number(surgery,"HealthPenaltyMin");var max=Native.Number(surgery,"HealthPenaltyMax");if(min>0&&max>=min)return UnityEngine.Random.Range(min,max)/100f;}
        throw new InvalidOperationException("The surgical kit's native HP penalty was unavailable.");
    }
    internal static void Surgery(ActiveHealthController h,EBodyPart part,float penalty)
    { foreach(var u in Uses.Values.Where(u=>u.State.Player==h.Player&&u.Part==part))u.SurgeryPenalty=penalty; }
    internal static void Maintain(ChestRigRuntime.State s,float dt)
    {
        var h=s.Player.ActiveHealthController;
        if(s.Key=="AR03-1"&&s.Active("triageHeal"))Heal(h,5f*dt);
        if(s.Key!="AR05-2")return;
        var bleeds=h.GetAllEffects().Where(e=>e.Existing&&e.GetType().Name is "LightBleeding" or "HeavyBleeding").ToArray();
        foreach(var bleed in bleeds)
        {
            if(!s.Bleeds.TryGetValue(bleed,out var until)){s.Bleeds[bleed]=Time.time+10f;continue;}
            if(Time.time>=until){Native.Call(bleed,"ForceRemove");s.Bleeds.Remove(bleed);}
        }
        foreach(var old in s.Bleeds.Keys.Where(e=>!bleeds.Contains(e)).ToArray())s.Bleeds.Remove(old);
    }
    private static readonly EBodyPart[] HealParts={EBodyPart.Head,EBodyPart.Chest,EBodyPart.Stomach,EBodyPart.LeftArm,EBodyPart.RightArm,EBodyPart.LeftLeg,EBodyPart.RightLeg};
    internal static void Heal(ActiveHealthController h,float budget)
    {
        var injured=HealParts.Where(p=>!h.IsBodyPartDestroyed(p)&&h.GetBodyPartHealth(p,false).Current>0f&&h.GetBodyPartHealth(p,false).Current<h.GetBodyPartHealth(p,false).Maximum).ToList();
        while(budget>.0001f&&injured.Count>0)
        {
            var share=budget/injured.Count;
            foreach(var part in injured.ToArray())
            {
                var health=h.GetBodyPartHealth(part,false);
                var amount=Math.Min(share,health.Maximum-health.Current);
                h.ChangeHealth(part,amount,DamageHelper.MedKitUse);budget-=amount;
                if(amount<share||h.GetBodyPartHealth(part,false).Current>=health.Maximum)injured.Remove(part);
            }
        }
    }
    internal static bool HasDressing(ChestRigRuntime.State s)=>ChestRigInventory.Carried(s).Any(i=>Supply(i,false)||Supply(i,true));
    private static bool Supply(Item item,bool heavy)
    {
        var med=Native.Component<MedKitComponent>(item);var effects=Native.Component<HealthEffectsComponent>(item);
        if(med?.HpResource is not >0||med.HpResourceRate>0f||effects is null)return false;
        return effects.DamageEffects.ContainsKey(heavy?EDamageEffectType.HeavyBleeding:EDamageEffectType.LightBleeding);
    }
    internal static void NewBleed(ActiveHealthController h,bool heavy,EBodyPart part)
    {
        var s=ChestRigRuntime.Get(h.Player);if(s?.Key!="AR24-1"||!s.DressingArmed)return;
        var effect=h.GetAllEffects().FirstOrDefault(e=>e.Existing&&e.BodyPart==part&&e.GetType().Name==(heavy?"HeavyBleeding":"LightBleeding"));
        var item=ChestRigInventory.Carried(s).FirstOrDefault(i=>Supply(i,heavy));
        if(effect is null||item is null)return;
        var med=Native.Component<MedKitComponent>(item)!;med.HpResource=Math.Max(0f,med.HpResource-1f);item.RaiseRefreshEvent();
        Native.Call(effect,"ForceRemove");s.DressingArmed=false;
    }
    internal static bool PauseBleed(ActiveHealthController h,EBodyPart part,EDamageType type)
    {
        var s=ChestRigRuntime.Get(h.Player);return s?.Key=="AR04-2"&&type is EDamageType.LightBleeding or EDamageType.HeavyBleeding&&Uses.Values.Any(u=>u.State==s&&u.Part==part&&Native.Component<HealthEffectsComponent>(u.Item)?.DamageEffects.ContainsKey(type==EDamageType.HeavyBleeding?EDamageEffectType.HeavyBleeding:EDamageEffectType.LightBleeding)==true);
    }
    internal static bool Combat(EDamageType type)=>type is EDamageType.Bullet or EDamageType.Explosion or EDamageType.GrenadeFragment or EDamageType.Melee;
    internal static float Incoming(ChestRigRuntime.State? s,EDamageType type,EBodyPart part)
    {
        if(s is null||!Combat(type))return 1f;
        var factor=1f;var p=s.Player;
        if(ChestRigRuntime.Knight(s))factor*=.9f;
        if(s.Key=="TR13-1"&&ChestRigRuntime.ThreeFirearms(p))factor*=.9f;
        if(s.Key=="AR07-1"&&type is EDamageType.Explosion or EDamageType.GrenadeFragment)factor*=.4f;
        if(s.Key=="AR18-2")factor*=1f-.05f*ChestRigRuntime.BlackParts(p);
        if(s.Key=="AR24-2"&&ChestRigRuntime.Health(p,EBodyPart.Chest)<.35f)factor*=.55f;
        if(s.Key=="AR26-2"&&ChestRigRuntime.Health(p)<.5f)factor*=.7f;
        if(s.Key=="AR11-1"&&s.Reloading&&type==EDamageType.Bullet&&part is EBodyPart.Chest or EBodyPart.Stomach)factor*=.8f;
        if(s.Key=="AR15-2"&&s.Active("door")&&type==EDamageType.Bullet&&part is EBodyPart.LeftLeg or EBodyPart.RightLeg)factor*=.75f;
        return factor;
    }
    internal static float UseSpeed(Player p,Item? item=null)
    {
        var s=ChestRigRuntime.Get(p);
        if(s?.Key=="TR28-2"&&s.Active("medical"))return 1.5f;
        item??=(p.HandsController as Player.MedsController)?.Item;
        return ServiceWindow.IsMedical(item)?ServiceWindow.Speed(p):1f;
    }
}
