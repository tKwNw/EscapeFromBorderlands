"""Audit source reflection literals and required dynamic members against native metadata."""
import argparse
import json
import re
from pathlib import Path
from ecma import Assembly


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--references', type=Path, required=True)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    assemblies = [Assembly(args.references / p) for p in (
        'Client/Assembly-CSharp.dll', 'Client/UnityEngine.CoreModule.dll', 'Client/UnityEngine.UI.dll', 'Client/mscorlib.dll', 'Client/UnityEngine.PhysicsModule.dll', 'Client/UnityEngine.AudioModule.dll')]
    types = {}
    for assembly in assemblies:
        for rid in range(1, len(assembly.tables[2])):
            types.setdefault(assembly.typename(2, rid), (assembly, rid))

    def resolve(name):
        name = {'object': 'System.Object'}.get(name, name)
        name = re.sub(r'<.*>', '`1', name)
        return [(n, pair) for n, pair in types.items()
                if n.replace('+', '.') == name or n.replace('+', '.').endswith('.' + name)]

    def owners(name):
        seen = set()
        while name in types and name not in seen:
            seen.add(name)
            assembly, rid = types[name]
            yield name, assembly, rid
            table, parent = assembly.coded('TypeDefOrRef', assembly.tables[2][rid]['Extends'])
            name = assembly.typename(table, parent).split('<', 1)[0] if parent and table in (1, 2, 27) else ''

    def members(name, kind, member):
        table = 4 if kind == 'Field' else 6
        names = [member] if kind in ('Field', 'Method') else ['get_' + member]
        if kind == 'Member':
            return members(name, 'Field', member) + members(name, 'Property', member)
        return [dict(owner=owner, signature=assembly.signature(table, rid))
                for owner, assembly, tid in owners(name)
                for rid, row in assembly.members(tid, table) if row['Name'] in names]

    checks, unresolved = [], []
    client = args.source / 'src/EscapeFromBorderlands.Client'
    for path in sorted(client.glob('*.cs')):
        text = path.read_text()
        pattern = r'AccessTools\.(Field|Property|PropertyGetter|Method)\(typeof\(([\w.<>]+)\),\s*"([^"]+)"'
        for match in re.finditer(pattern, text):
            kind, type_name, member = match.groups()
            candidates = resolve(type_name)
            if len(candidates) != 1:
                unresolved.append(dict(file=path.name, type=type_name, member=member))
                continue
            full_name = candidates[0][0]
            found = members(full_name, kind, member)
            checks.append(dict(file=path.name, kind=kind, type=full_name, member=member,
                               status='PASS' if found else 'FAIL', matches=found))

    # Native.Get/Call use runtime receiver types. Record each required receiver
    # explicitly; optional cross-version Actor fallbacks are intentionally excluded.
    contracts = [
        ('Field', 'EFT.HealthSystem.ActiveHealthController+MedEffect', '_interrupted'),
        ('Field', 'EFT.Player', 'LastAggressor'),
        ('Member', 'EFT.Player', 'LastDamageInfo'),
        ('Member', 'EFT.GameWorld', 'LocationId'),
        ('Field', 'EFT.GameWorld', 'GameDateTime'),
        ('Method', 'EFT.GameDateTime', 'Calculate'),
        ('Field', 'BaseSoundPlayer', 'playersBridge'),
        ('Property', 'BaseSoundPlayer+IObserverToPlayerBridge', 'iPlayer'),
        ('Field', 'WeaponSoundPlayer', '_queue'),
        ('Field', 'EFT.Player', '_gearSource'),
        ('Field', 'EFT.Player', '_searchSource'),
        ('Field', 'EFT.Player', 'NestedStepSoundSource'),
        ('Property', 'EFT.Player', 'SpeechSource'),
        ('Field', 'EFT.Animations.NewRotationRecoilProcess', '_autoFireSpeed'),
        ('Field', 'EFT.Animations.ProceduralWeaponAnimation', '_aimSwayBlender'),
        ('Method', 'UnityEngine.AudioClip', 'Create'),
        ('Method', 'UnityEngine.AudioClip', 'SetData'),
        ('Property', 'UnityEngine.AudioSource', 'panStereo'),
        ('Field', 'EFT.InteractionContextHelper+CG_GetAvailableActions5', 'resultItem'),
        ('Field', 'EFT.Player+BaseGrenadeHandsController', 'GrenadeEmission'),
        ('Field', 'EFT.Player+BaseGrenadeHandsController', '_grenadeTransform'),
        ('Field', 'EFT.Player+BaseGrenadeHandsController', 'ThrowingParts'),
        ('Method', 'EFT.Player+BaseGrenadeHandsController', 'Cook'),
        ('Method', 'EFT.Player+BaseGrenadeHandsController', 'StartCountdown'),
        ('Field', 'EFT.Player+PlayerInventoryController+LoadMagazineProcess', '_inventoryController'),
        ('Field', 'EFT.Player+PlayerInventoryController+UnloadMagazineProcess', '_inventoryController'),
        ('Field', 'EFT.Player+PlayerInventoryController+LoadMagazineProcess', '_cooldown'),
        ('Field', 'EFT.Player+PlayerInventoryController+UnloadMagazineProcess', '_cooldown'),
        ('Member', 'SmokeGrenade', '_smokeGrenadeSettings'),
        ('Field', 'SmokeGrenadeSettings', '_emissionArea'),
        ('Field', 'GrenadeEmission', '_defaultStartFillDelay'),
        ('Field', 'GrenadeEmission', '_startFillSize'),
        ('Field', 'GrenadeEmission', '_startFillDistance'),
        ('Field', 'GrenadeEmission', '_particleSystem'),
        ('Member', 'Throwable', 'Rigidbody'),
        ('Member', 'Throwable', 'Velocity'),
        ('Member', 'UnityEngine.Collision', 'collider'),
        ('Member', 'UnityEngine.Collision', 'contacts'),
        ('Member', 'UnityEngine.ContactPoint', 'normal'),
        ('Field', 'Physical', '_player'),
        ('Field', 'EFT.Player+ItemHandsController', '_player'),
        ('Field', 'EFT.Player+PlayerInventoryController', 'Player'),
        ('Member', 'Physical', 'HoldingBreath'),
        ('Field', 'EFT.InventoryLogic.LightComponent', 'IsActive'),
        ('Member', 'EFT.Animations.ProceduralWeaponAnimation', 'CurrentScope'),
        ('Member', 'EFT.Animations.ProceduralWeaponAnimation+SightNBone', 'IsOptic'),
        ('Field', 'EFT.Animations.ProceduralWeaponAnimation', '_aimSwayBlender'),
        ('Field', 'EFT.Player', '_mouseSensitivityModifier'),
        ('Member', 'ObjectInHandsAnimator', 'Animator'),
        ('Field', 'EFT.InventoryLogic.SlotItemAddress', 'Slot'),
        ('Member', 'EFT.InventoryLogic.Slot', 'ID'),
        ('Field', 'JsonType.DamageEffectSpecification', 'HealthPenaltyMin'),
        ('Field', 'JsonType.DamageEffectSpecification', 'HealthPenaltyMax'),
        ('Member', 'EFT.InventoryLogic.Grid', 'GridWidth'),
        ('Member', 'EFT.InventoryLogic.Grid', 'GridHeight'),
        ('Field', 'BaseSoundPlayer', 'playersBridge'),
        ('Method', 'EFT.GameDateTime', 'Calculate'),
        ('Member', 'EFT.HealthSystem.ActiveHealthController+MedEffect', 'MedItem'),
        ('Member', 'EFT.HealthSystem.ActiveHealthController+MedEffect', 'HealthController'),
        ('Member', 'EFT.HealthSystem.ActiveHealthController+MedEffect', 'BodyPart'),
        ('Field', 'EFT.HealthSystem.ActiveHealthController+MedEffect', '_interrupted'),
        ('Property', 'EFT.InventoryLogic.HealthEffectsComponent', 'HealthEffects'),
        ('Member', 'EFT.HealthSystem.ActiveHealthController', 'UpdateTime'),
        ('Member', 'EFT.HealthSystem.ActiveHealthController+Effect', 'Existing'),
        ('Member', 'EFT.HealthSystem.ActiveHealthController+Effect', 'TimeLeft'),
        ('Method', 'EFT.HealthSystem.ActiveHealthController+Effect', 'ForceRemove'),
        ('Method', 'EFT.HealthSystem.ActiveHealthController+PainKiller', 'StoreValues'),
        ('Member', 'EFT.Player+SlotObserver`1', 'Component'),
        ('Field', 'EFT.UI.CharacterHealthPanel', '_effectIconTemplate'),
        ('Field', 'EFT.UI.CharacterHealthPanel', '_effectsPanel'),
        ('Field', 'EFT.UI.StackedEffectIcon', '_stackCount'),
        ('Property', 'EFT.UI.CharacterHealthPanel', 'AutoHide'),
        ('Method', 'EFT.UI.CharacterHealthPanel', 'AnimatedShow'),
        ('Field', 'EFT.UI.DragAndDrop.TemplatedGridsView', '_presetGridViews'),
        ('Method', 'EFT.Utilities.ResourcesCache', 'Pop'),
        ('Method', 'UnityEngine.UI.LayoutRebuilder', 'ForceRebuildLayoutImmediate'),
        ('Method', 'UnityEngine.UI.LayoutRebuilder', 'MarkLayoutForRebuild'),
        ('Member', 'EFT.InventoryLogic.CompoundItem', 'Grids'),
        ('Property', 'EFT.InventoryLogic.Grid', 'Items'),
        ('Field', 'CommonAssets.Scripts.Game.EndByExitTrigerScenario', '_game'),
        ('Field', 'CommonAssets.Scripts.Game.EndByExitTrigerScenario', '_escapes'),
        ('Field', 'CommonAssets.Scripts.Game.EndByExitTrigerScenario', 'list_1'),
        ('Field', 'CommonAssets.Scripts.Game.EndByExitTrigerScenario', '_monitoredEP'),
        ('Field', 'EFT.BaseLocalGame`1', '_endByExitTrigerScenario'),
        ('Field', 'EFT.SynchronizableObjects.TripwireSynchronizableObject', '_originalGrenade'),
        ('Field', 'EFT.InteractionContextHelper+CG_GetAvailableActions7', 'owner'),
        ('Field', 'EFT.InteractionContextHelper+CG_GetAvailableActions7', 'plantTime'),
        ('Field', 'EFT.InteractionContextHelper+CG_GetAvailableActions7', 'tripwire'),
        ('Member', 'EFT.GamePlayerOwner', 'Player'),
        ('Member', 'EFT.Player+BaseGrenadeHandsController', 'ThrowingParts'),
        ('Method', 'GlobalEventDispatcher', 'ThrowGrenade'),
        ('Method', 'EFT.Interactive.ExfiltrationPoint', 'OnItemTransferred'),
        ('Method', 'Audio.AmbientSubsystem.WindAmbientBlender', 'UpdateWindSound'),
        ('Method', 'Audio.AmbientSubsystem.PrecipitationAmbientBlender', 'StartCrossfader'),
        ('Member', 'BaseSoundPlayer+IObserverToPlayerBridge', 'iPlayer'),
    ]
    contracts += [
        ('Field', 'ParticleSystemAdapter', '_lifeTimes'),
        ('Field', 'ParticleSystemAdapter', '_speeds'),
        ('Field', 'ParticleSystemAdapter', '_sizes'),
        ('Field', 'EFT.Particles.BasicParticleSystemMediator', '_particleSystems'),
        ('Field', 'EFT.Particles.BasicParticleSystemMediator', '_index'),
        ('Field', 'EFT.Player', '_specificStepAudioController'),
        ('Field', 'Audio.AmbientSubsystem.PlayerStepLayerAudioController', '_source'),
        ('Field', 'SuperSource', '_secondPreEffectsVolume'),
        ('Field', 'SuperSourceDistant', '_secondPreEffectsVolume'),
    ]
    recoil_text = (client / 'BackpackPresentation.cs').read_text()
    if 'NightVisionOn' in recoil_text:
        contracts += [('Member', 'EFT.InventoryLogic.NightVisionComponent', 'Togglable'),
                      ('Field', 'EFT.InventoryLogic.TogglableComponent', 'On')]
    else:
        contracts += [('Member', 'EFT.InventoryLogic.NightVisionComponent', 'IsOn')]
    recoil_member = '_firearmController' if '____firearmController' in recoil_text else 'FirearmController'
    contracts += [('Member', name, recoil_member) for name in (
        'EFT.Animations.NewRecoil.NewRecoilShotEffect', 'EFT.Animations.OldRecoilShotEffect')]
    # Bind the literal actually used by the new rig's reflected receivers.
    # A remembered legacy alias must fail against the target metadata.
    medical = client / 'ChestRigMedical.cs'
    if medical.exists():
        for member in sorted(set(re.findall(r'Native.Get\(effect,\s*"([^"\n]+)"', medical.read_text()))):
            contracts.append(('Member', 'EFT.HealthSystem.ActiveHealthController+MedEffect', member))
    rig_runtime = client / 'ChestRigRuntime.cs'
    if rig_runtime.exists():
        for member in re.findall(r'Native.Get\(Native.Get\(p.HandsController,"CurrentOperation"\),"([^"\n]+)"', rig_runtime.read_text()):
            contracts.append(('Member', 'EFT.Player+FirearmController+UtilityOperation', member))
    dynamic = []
    for kind, type_name, member in contracts:
        found = members(type_name, kind, member)
        dynamic.append(dict(kind=kind, type=type_name, member=member,
                            status='PASS' if found else 'FAIL', matches=found))
    failures = [c for c in checks + dynamic if c['status'] == 'FAIL']
    report = dict(scope=f'Native metadata checks for {len(checks)} typed reflection calls and {len(dynamic)} dynamic receiver bindings; optional Actor aliases and unavailable TMP implementation are not execution tests',
                  source=args.source.name, passed=len(checks) + len(dynamic) - len(failures),
                  failed=len(failures) + len(unresolved), typed=checks, dynamic=dynamic,
                  unresolved=unresolved, failures=failures)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(dict(passed=report['passed'], failed=report['failed'], failures=failures, unresolved=unresolved), indent=2))
    return int(bool(report['failed']))


if __name__ == '__main__':
    raise SystemExit(main())
