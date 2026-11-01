using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// Inspect metadata only. Never execute EFT, Unity or production Harmony patches here.
internal static class NativeBindings
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal static object Validate(string references, string clientDll, string? uiaDll = null)
    {
        var paths = Directory.GetFiles(references, "*.dll", SearchOption.AllDirectories)
            .Append(clientDll)
            .Concat(uiaDll is null ? Array.Empty<string>() : new[] { uiaDll })
            .Concat(((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
            .Select(Path.GetFullPath).GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).Select(g => g.First());
        using var context = new MetadataLoadContext(new PathAssemblyResolver(paths), "mscorlib");
        var game = context.LoadFromAssemblyPath(Path.Combine(references, "Client/Assembly-CSharp.dll"));
        var client = context.LoadFromAssemblyPath(clientDll);
        var uia = uiaDll is null ? null : context.LoadFromAssemblyPath(uiaDll);
        using var pe = new PEReader(File.OpenRead(Path.Combine(references, "Client/Assembly-CSharp.dll")));
        var metadata = pe.GetMetadataReader();
        context.LoadFromAssemblyPath(Path.Combine(references, "Client/SPT/spt-common.dll"));
        Type T(string name) => game.GetType(name) ?? context.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null) ?? throw new Exception("Missing native type " + name);
        Type Scalar() => context.CoreAssembly!.GetType("System.Single", true)!;
        MethodBase Getter(string type, string property) => T(type).GetProperty(property, All)!.GetMethod!;
        MethodBase Iterator(MethodBase method) {
            var state = method.GetCustomAttributesData().First(a => a.AttributeType.Name is "IteratorStateMachineAttribute" or "AsyncStateMachineAttribute");
            return ((Type)state.ConstructorArguments[0].Value!).GetMethod("MoveNext", All)!;
        }
        IEnumerable<Type> LocalDescendants(string name)
        {
            // Follow the actual local TypeDef inheritance graph. This avoids resolving
            // unrelated Odin/voice/graphics classes absent from the reference export.
            var root = MetadataTokens.EntityHandle(T(name).MetadataToken);
            var matching = new HashSet<int>();
            foreach (var definition in metadata.TypeDefinitions)
            {
                EntityHandle current = definition;
                while (current.Kind == HandleKind.TypeDefinition)
                {
                    if (current == root) { matching.Add(MetadataTokens.GetToken(definition)); break; }
                    current = metadata.GetTypeDefinition((TypeDefinitionHandle)current).BaseType;
                    if (current.IsNil) break;
                }
            }
            return game.GetTypes().Where(t => matching.Contains(t.MetadataToken));
        }
        MethodBase Method(string type, string name, params string[] parameters)
        {
            var methods = T(type).GetMethods(All | BindingFlags.DeclaredOnly).Where(m => m.Name == name &&
                (parameters.Length == 0 || m.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(parameters))).ToArray();
            if (methods.Length != 1) throw new Exception($"Expected one native target: {type}.{name}; found {methods.Length}");
            return methods[0];
        }
        const string Controller = "EFT.Player+FirearmController";
        const string Fire = Controller + "+FireOperation";
        var dynamicTargets = new Dictionary<string, Func<IEnumerable<MethodBase>>>
        {
            ["LegendaryEarpieceAttributePatch"] = () => T("EFT.InventoryLogic.Headphones").GetConstructors(All).Where(c => c.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.String", "EFT.InventoryLogic.HeadphonesTemplate" })),
            // HollywoodFX is optional and absent from the supplied native references.
            ["LegendaryHollywoodFxBlastPatch"] = () => Array.Empty<MethodBase>(),
            ["UseItemsAnywhereGrenadeWheelPatch"] = () => uia is null ? Array.Empty<MethodBase>() : new[] { uia.GetType("UseItemsAnywhere.QuickUseWheel.QuickUseWheelInventory",true)!.GetMethods(All).Single(m => m.Name == "HasResource" && m.IsStatic && m.ReturnType.FullName == "System.Boolean" && m.GetParameters().Select(p=>p.ParameterType.FullName).SequenceEqual(new[] { "EFT.InventoryLogic.Item" })) },
            ["LegendaryGrenadeAttributePatch"] = () => T("EFT.InventoryLogic.ThrowWeap").GetConstructors(All).Where(c => c.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.String", "EFT.InventoryLogic.ThrowWeapTemplate" })),
            ["LegendaryQuestPlacePatch"] = () => new[] { Method("EFT.InteractionContextHelper+CG_GetAvailableActions4", "method_0") },
            ["LegendaryQuestPlaceCommitPatch"] = () => new[] { Method("EFT.InteractionContextHelper+CG_GetAvailableActions5", "method_0") },
            ["LegendaryQuestWorldItemPatch"] = () => new[] { Method("EFT.GameWorld+CG_ManageQuestLoot", "MoveNext") },
            ["LegendaryQuestItemMatchPatch"] = () => new[] { T("EFT.Quests.ConditionsConnectorsManager`1").MakeGenericType(T("EFT.Quests.Quest")).GetMethod("ContainsPass", All)! },
            ["LegendaryQuestSellPatch"] = () => new[] { T("EFT.Quests.ConditionalController`1").MakeGenericType(T("EFT.Quests.Quest")).GetMethod("IncreaseSellToTraderCounter", All)! },
            ["EarpieceFireRatePatch"] = () => new[] { Getter("EFT.InventoryLogic.Weapon", "FireRate"), Getter("EFT.InventoryLogic.Weapon", "SingleFireRate") },
            ["EarpieceRecoilRecoveryPatch"] = () => LocalDescendants("EFT.Animations.RecoilProcessBase").SelectMany(t => t.GetMethods(All | BindingFlags.DeclaredOnly)).Where(m => m.Name == "CalculateRecoil"),
            ["EarpieceFoodStartPatch"] = () => new[] { Method("EFT.HealthSystem.ActiveHealthController+MedEffect", "Started") },
            ["EarpieceFoodEndPatch"] = () => new[] { Method("EFT.HealthSystem.ActiveHealthController+MedEffect", "Residue") },
            ["EarpieceStepScopePatch"] = () => new[] { Method("EFT.Player", "PlayStepSound"), Method("EFT.Player", "DefaultPlay"), Method("EFT.Player", "PlayGearSound", "EFT.SoundBank", "System.Single") },
            ["EarpieceGunScopePatch"] = () => new[] { Method("WeaponSoundPlayer", "FireBullet"), Method("WeaponSoundPlayer", "StopFiringLoop") },
            ["EarpieceQueueSourcePatch"] = () => new[] { Method("SuperBetterAudioQueue", "SetRolloff"), Method("SuperBetterAudioQueue", "Enqueue") },
            ["EarpieceSourcePlaybackPatch"] = () => new[] { Method("BetterSource", "Play"), Method("BetterSource", "PlayScheduled") },
            ["EarpieceSourceVolumePatch"] = () => new[] { Method("BetterSource", "UpdateSourceVolume"), Method("SuperSource", "UpdateSourceVolume"), Method("SuperSourceDistant", "UpdateSourceVolume") },
            ["EarpieceSourceRangePatch"] = () => new[] { Method("SimpleSource", "SetRolloff"), Method("SuperSource", "SetRolloff"), Method("SuperSourceDistant", "SetRolloff") },
            ["GrenadePackingDelayPatch"] = () => new[] { Method("EFT.Player+PlayerInventoryController+LoadMagazineProcess", "method_5"), Method("EFT.Player+PlayerInventoryController+UnloadMagazineProcess", "method_4") },
            ["ReusableGrenadeSelectorResultPatch"] = () => new[] { Method("EFT.UI.GrenadeSelector", "GetGrenade") },
            ["ReusableGrenadeBotInventoryPatch"] = () => new[] { Method("BotGrenadeController", "HaveGrenadeOfType"), Method("BotGrenadeController", "GetGrenadeCount"), Method("BotGrenadeController", "CheckGrenadeWithType"), Method("BotGrenadeController", "CheckGrenade") },
            ["ChestRigDoorPatch"] = () => new[] { Method("EFT.BreachDoorState", "ExecuteDoorInteraction"), Method("EFT.DoorInteractState", "ExecuteDoorInteraction") },
            ["ChestRigInteractionEndPatch"] = () => new[] { Method("EFT.BreachDoorState", "Exit"), Method("EFT.DoorInteractState", "Exit") },
            ["ChestRigFleshHitPatch"] = () => new[] { Method("BodyPartCollider", "ApplyHit"), Method("ArmorPlateCollider", "ApplyHit") },
            ["ChestRigFireRatePatch"] = () => new[] { Getter("EFT.InventoryLogic.Weapon", "FireRate"), Getter("EFT.InventoryLogic.Weapon", "SingleFireRate") },
            ["ChestRigMedStartPatch"] = () => new[] { Method("EFT.HealthSystem.ActiveHealthController+MedEffect", "Started") },
            ["ChestRigMedEndPatch"] = () => new[] { Method("EFT.HealthSystem.ActiveHealthController+MedEffect", "Residue") },
            ["ChestRigBleedPatch"] = () => new[] { Method("EFT.HealthSystem.ActiveHealthController", "DoBleed", "System.Boolean", "EBodyPart") },
            ["ChestRigMedicalCostPatch"] = () => new[] { Method("EFT.HealthSystem.ActiveHealthController+MedEffect", "RegularUpdate") },
            ["BackpackCachedValuePatch"] = () => new[] { T("Compute`1").MakeGenericType(Scalar()).GetProperty("Value",All)!.GetMethod!, T("Deferred`1").MakeGenericType(Scalar()).GetProperty("Value",All)!.GetMethod! },
            ["BackpackMedRestorationScopePatch"] = () => new[] { Method("EFT.HealthSystem.ActiveHealthController+MedEffect", "RegularUpdate") },
            ["BackpackMedBeginPatch"] = () => new[] { Method("EFT.HealthSystem.ActiveHealthController+MedEffect", "Started") },
            ["BackpackMedCompletePatch"] = () => new[] { Method("EFT.HealthSystem.ActiveHealthController+MedEffect", "Residue") },
            ["BackpackPainkillerDoseCapPatch"] = () => new[] { Method("EFT.HealthSystem.ActiveHealthController+PainKiller", "StoreValues") },
            ["BackpackUseTimePatch"] = () => new[] { Getter("EFT.InventoryLogic.HealthEffectsComponent", "UseTime") },
            ["BackpackDoBleedPatch"] = () => new[] { Method("EFT.HealthSystem.ActiveHealthController", "DoBleed", "System.Boolean", "EBodyPart") },
            ["BackpackFracturePatch"] = () => new[] { Method("EFT.HealthSystem.ActiveHealthController", "DoFracture") },
            ["BackpackJigsawKitAvailabilityPatch"] = () => new[] { Method("EFT.Player+BaseGrenadeHandsController", "GetPlantingKit") },
            ["BackpackJigsawPlacementPatch"] = () => new[] { Method("EFT.InventoryLogic.Operations.PlantTripwireOperation", "ExecuteInternal") },
            ["BackpackFreeGrenadeSelectionPatch"] = () => new[] { Method("EFT.UI.DragAndDrop.FastAccessGrenadeItemView", "EFT.ISetInHandsHandler.OnSetInHands", "EFT.InventoryLogic.SetInHandsEventArgs") },
            ["BackpackDisarmSpeedPatch"] = () => new[] { Method("EFT.InteractionContextHelper+CG_GetAvailableActions7", "method_0") },
            ["BackpackDisarmedGrenadePatch"] = () => T("EFT.GameWorld").GetMethods(All).Where(m => m.Name == "ThrowItem" && m.GetParameters().Length == 9),
            ["BackpackRecoilSettlingPatch"] = () => new[] { Method("EFT.Animations.NewRecoil.NewRecoilShotEffect", "FixedUpdate"), Method("EFT.Animations.OldRecoilShotEffect", "FixedUpdate") },
            ["BackpackRaidRequestPatch"] = () => T("SPT.Common.Http.RequestHandler").GetMethods(All).Where(m => m.IsPublic && m.IsStatic && m.Name is "PostJson" or "PostJsonAsync" or "PutJson" or "PutJsonAsync"),
            ["BackpackSearchCompletionPatch"] = () => new[] { Method("EFT.SearchController", "InvokeItemSearched") },
            ["BackpackConcreteInventorySearchPatch"] = () => new[] { Method("EFT.PlayerInputTranslator", "vmethod_1"), Method("EFT.FirearmHandsInputTranslator", "ReloadExternalMagazine"), Method("BotReload", "GetMagazineForReload"), Method("BotReload", "AddAmmoToMagazines") },
            ["BackpackInventorySoundScopePatch"] = () => T("EFT.UI.GUISounds").GetMethods(All).Where(m => m.IsPublic && m.Name == "PlayItemSound"),
            ["BackpackWeatherTargetVolumePatch"] = () => new[] { Getter("Audio.AmbientSubsystem.PrecipitationAmbientBlender", "CurrentTargetVolume"), Method("Audio.AmbientSubsystem.WindAmbientBlender", "GetWindVolume") },
            ["BackpackWeatherRefreshPatch"] = () => new[] { Method("Audio.AmbientSubsystem.PrecipitationAmbientBlender", "ManualUpdate"), Method("Audio.AmbientSubsystem.WindAmbientBlender", "ManualUpdate") },
            ["BackpackWeatherOneShotPatch"] = () => new[] { Iterator(Method("Audio.AmbientSubsystem.BaseAmbientSoundPlayer", "PlayCoroutine")) },
            ["BackpackLegMovementDamagePatch"] = () => new[] { Method("EFT.Player", "MovementContextOnStateChanged"), Iterator(Method("EFT.Player", "InflictSelfDamage")) },
            ["BackpackMagazineSpeedPatch"] = () => T("EFT.Player+PlayerInventoryController").GetMethods(All | BindingFlags.DeclaredOnly).Where(m => m.Name is "LoadMagazine" or "UnloadMagazine").Select(Iterator),
            ["LegendaryMeleeHealthPatch"] = () => new[] { Method("EFT.HealthSystem.ActiveHealthController", "ApplyDamage", "EBodyPart", "System.Single", "EFT.Ballistics.DamageInfo") },
            ["LegendaryMeleeAttributePatch"] = () => T("EFT.InventoryLogic.KnifeComponent").GetConstructors(All)
                .Where(m => m.GetParameters().Select(p => p.ParameterType.FullName)
                    .SequenceEqual(new[] { "EFT.InventoryLogic.Item", "EFT.InventoryLogic.IKnifeComponentTemplate" })),
            ["WeaponFireRateAttributePatch"] = () => T("EFT.InventoryLogic.Weapon").GetConstructors(All)
                .Where(m => m.GetParameters().Select(p => p.ParameterType.FullName)
                    .SequenceEqual(new[] { "System.String", "EFT.InventoryLogic.WeaponTemplate" })),
            ["LegendaryFireModePatch"] = () => LocalDescendants(Controller)
                .SelectMany(t => t.GetMethods(All | BindingFlags.DeclaredOnly))
                .Where(m => m.Name == "SetTriggerPressed" && m.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.Boolean" })),
            ["LegendaryCarrierAnimationPatch"] = () => LocalDescendants(Fire)
                .SelectMany(t => t.GetMethods(All | BindingFlags.DeclaredOnly))
                .Where(m => m.Name is "Start" or "PrepareShot" or "OnFireEvent" or "OnAddAmmoInChamber" or "OnFireEndEvent" or "OnIdleStartEvent" or "Update" or "FastForward"),
            ["LegendaryFireModeNamePatch"] = () => new[] { Method("EFT.UI.AmmoCountPanel", "ShowFireMode", "EFT.InventoryLogic.Weapon+EFireMode") },
            ["LegendaryMissTrackingPatch"] = () => new[] { Method("EFT.Ballistics.BallisticsCalculator", "RemovePredicate", "EFT.Ballistics.Shot") },
            ["LegendaryFleshHitPatch"] = () => new[] { Method("BodyPartCollider", "ApplyHit"), Method("ArmorPlateCollider", "ApplyHit") },
            ["LegendaryRocketDirectHitPatch"] = () => new[] { Method("EFT.RocketLauncher.RocketProjectile", "ProceedDamage") },
            ["LegendaryReloadCompletionPatch"] = () => new[] {
                Method(Controller,"ReloadMag","EFT.InventoryLogic.Magazine","EFT.InventoryLogic.ItemAddress","Comfort.Common.Callback"),
                Method(Controller,"QuickReloadMag","EFT.InventoryLogic.Magazine","Comfort.Common.Callback"),
                Method(Controller,"ReloadWithAmmo","EFT.InventoryLogic.AmmoPack","Comfort.Common.Callback"),
                Method(Controller,"ReloadCylinderMagazine","EFT.InventoryLogic.AmmoPack","Comfort.Common.Callback","System.Boolean"),
                Method(Controller,"ReloadBarrels","EFT.InventoryLogic.AmmoPack","EFT.InventoryLogic.ItemAddress","Comfort.Common.Callback"),
                Method(Controller,"ReloadGrenadeLauncher","EFT.InventoryLogic.AmmoPack","Comfort.Common.Callback") },
            ["ItemDescriptionPatch"] = () => new[] { Method("EFT.UI.ItemSpecificationPanel", "method_1") },
            ["InspectItemNameColorPatch"] = () => new[] { Method("EFT.UI.ItemSpecificationPanel", "method_0") },
            ["GridItemNameColorPatch"] = () => new[] { Method("EFT.UI.DragAndDrop.GridItemView", "GetItemName", "EFT.InventoryLogic.ItemAddress") },
        };
        int patchClasses = 0, nativeTargets = 0, parametersChecked = 0;
        var bindings = new List<object>();
        var failures = new List<object>();
        var skipped = new List<object>();
        foreach (var patch in client.GetTypes())
        {
            var attributes = patch.GetCustomAttributesData().Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ToArray();
            if (attributes.Length == 0) continue;
            patchClasses++;
            try {
            MethodBase[] targets;
            var declaration = attributes.Single();
            if (declaration.ConstructorArguments.Count == 0 || dynamicTargets.ContainsKey(patch.Name))
            {
                if (!dynamicTargets.TryGetValue(patch.Name, out var resolve)) throw new Exception("Unmapped dynamic patch " + patch.Name);
                targets = resolve().ToArray();
            }
            else
            {
                var type = (Type)declaration.ConstructorArguments[0].Value!;
                var name = (string)declaration.ConstructorArguments[1].Value!;
                if (declaration.ConstructorArguments.Count == 3)
                {
                    if (declaration.ConstructorArguments[2].ArgumentType.IsArray)
                    {
                        var types = ((IEnumerable<CustomAttributeTypedArgument>)declaration.ConstructorArguments[2].Value!)
                            .Select(value => ((Type)value.Value!).FullName!).ToArray();
                        targets = new[] { types.Length == 0
                            ? type.GetMethods(All | BindingFlags.DeclaredOnly).Single(m => m.Name == name && m.GetParameters().Length == 0)
                            : Method(type.FullName!, name, types) };
                        goto ValidateTargets;
                    }
                    if (Convert.ToInt32(declaration.ConstructorArguments[2].Value) != 1) throw new Exception("Unexpected Harmony method kind");
                    name = "get_" + name;
                }
                targets = new[] { Method(type.FullName!, name) };
            }
            ValidateTargets:
            if (targets.Length == 0 && patch.Name is "LegendaryHollywoodFxBlastPatch" or "UseItemsAnywhereGrenadeWheelPatch")
            {
                skipped.Add(new { patch = patch.Name, reason = "Optional renderer/UIA assembly absent; controlled adapter tests run separately" });
                continue;
            }
            if (targets.Length == 0) throw new Exception("Empty dynamic target set: " + patch.Name);
            foreach (var target in targets)
            {
                nativeTargets++;
                foreach (var hook in patch.GetMethods(All | BindingFlags.DeclaredOnly).Where(m => m.Name is "Prefix" or "Postfix" or "Finalizer"))
                foreach (var parameter in hook.GetParameters())
                {
                    var name = parameter.Name!;
                    var expected = Element(parameter.ParameterType);
                    Type actual;
                    if (name == "__state" || name == "__originalMethod" || name == "__exception" || name == "__args") continue;
                    if (name == "__instance")
                    {
                        if (target.IsStatic) throw new Exception($"{patch.Name}: instance on static target");
                        actual = target.DeclaringType!;
                    }
                    else if (name == "__result") actual = ((MethodInfo)target).ReturnType;
                    else if (name.StartsWith("___", StringComparison.Ordinal))
                    {
                        var field = target.DeclaringType!.GetField(name[3..], All | BindingFlags.DeclaredOnly);
                        if (field is null) throw new Exception($"{patch.Name}: missing injected field {name[3..]}");
                        actual = field.FieldType;
                    }
                    else
                    {
                        ParameterInfo? original;
                        if (name.StartsWith("__", StringComparison.Ordinal) && int.TryParse(name[2..], out var index))
                            original = target.GetParameters().ElementAtOrDefault(index);
                        else original = target.GetParameters().SingleOrDefault(p => p.Name == name);
                        if (original is null) throw new Exception($"{patch.Name}: native argument '{name}' missing on {target.DeclaringType}.{target.Name}");
                        actual = Element(original.ParameterType);
                    }
                    if (expected.FullName != actual.FullName && expected.FullName != "System.Object" &&
                        !(expected.Assembly == game && actual.Assembly == game && LocalDescendants(expected.FullName!).Any(t => t.FullName == actual.FullName)) &&
                        !expected.IsAssignableFrom(actual))
                        throw new Exception($"{patch.Name}.{hook.Name} parameter {name}: {expected.FullName} cannot receive {actual.FullName}");
                    parametersChecked++;
                }
                bindings.Add(new { patch = patch.Name, target = target.DeclaringType!.FullName + "." + target.Name,
                    parameters = target.GetParameters().Select(p => new { p.Name, type = p.ParameterType.FullName }).ToArray() });
            }
            } catch (Exception e) { failures.Add(new { patch=patch.Name, error=e.GetBaseException().Message }); }
        }
        if (patchClasses < 57) throw new Exception($"Unexpected patch count: {patchClasses}");
        // These members are reflected by the new gameplay helpers rather than Harmony.
        // Inspect the actual target metadata; do not invoke EFT or Unity here.
        var reflectionBindings = new List<string>();
        void Field(string type, string name, string expected) {
            var field = T(type).GetField(name, All)!;
            if (field?.FieldType.FullName != expected) throw new Exception($"Missing reflected field {type}.{name}");
            reflectionBindings.Add(type + "." + name);
        }
        Field("Physical", "_player", "EFT.Player");
        Field("EFT.Player+KnifeRaycastHit", "collider", "UnityEngine.Collider");
        var playerRay = T("EFT.Player").GetProperty("InteractionRay", All)!;
        if (playerRay?.PropertyType.FullName != "UnityEngine.Ray") throw new Exception("Native interaction ray changed");
        reflectionBindings.Add("EFT.Player.InteractionRay");
        var finder = Method("EFT.GameWorld", "FindInteractable", "UnityEngine.Ray", "UnityEngine.RaycastHit&");
        if (!finder.IsStatic || ((MethodInfo)finder).ReturnType.FullName != "UnityEngine.GameObject" ||
            finder.GetParameters()[1].ParameterType.GetElementType()!.GetProperty("distance")?.PropertyType.FullName != "System.Single")
            throw new Exception("Native interaction probe shape changed");
        reflectionBindings.Add("EFT.GameWorld.FindInteractable / RaycastHit.distance");
        var health = T("EFT.HealthSystem.ActiveHealthController");
        var painkiller = health.GetNestedType("PainKiller", BindingFlags.Public | BindingFlags.NonPublic)!;
        var factory = health.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Single(m => m.Name == "AddEffect" && m.IsGenericMethodDefinition);
        if (painkiller is null || !painkiller.IsNestedPublic || factory.GetParameters().Length != 6)
            throw new Exception("Native painkiller factory changed");
        reflectionBindings.Add("ActiveHealthController.AddEffect<T> / public PainKiller");
        return new { patchClasses, nativeTargets, parametersChecked, bindings, reflectionBindings, skipped, failed=failures.Count, failures };
    }
    private static Type Element(Type type) => type.IsByRef ? type.GetElementType()! : type;
}
