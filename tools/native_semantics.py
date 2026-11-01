"""Check the roles of native members used by the port without executing EFT."""
import argparse
import json
import re
from pathlib import Path
from ecma import Assembly


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--references', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    game = Assembly(args.references / 'Client/Assembly-CSharp.dll')
    checks = []

    def il(type_name, method, signature=None, assembly=game):
        tid = assembly.find('^' + re.escape(type_name) + '$')[0][0]
        found = [rid for rid, row in assembly.members(tid, 6)
                 if row['Name'] == method and (signature is None or assembly.signature(6, rid) == signature)]
        if len(found) != 1:
            raise ValueError(f'Ambiguous/missing method {type_name}.{method}')
        return '\n'.join(assembly.il(found[0]))

    def check(name, ok):
        checks.append({'name': name, 'status': 'PASS' if ok else 'FAIL'})

    controller = 'EFT.Player+FirearmController'
    modified = il(controller, 'WeaponModified')
    shot = il(controller, 'Shot')
    check('Native accuracy cache receives GetTotalCenterOfImpact(false)',
          bool(re.search(r'ldc.i4.0\n[^\n]*GetTotalCenterOfImpact[^\n]*\n[^\n]*stfld[^\n]*::float_3', modified)))
    check('Native pellet dispersion cache receives TotalShotgunDispersion',
          bool(re.search(r'get_TotalShotgunDispersion[^\n]*\n[^\n]*stfld[^\n]*::float_4', modified)))
    check('Cartridge Shot reads both caches before InitiateShot',
          0 <= shot.find('::float_3') < shot.find('::InitiateShot') and
          0 <= shot.find('::float_4') < shot.find('::InitiateShot'))
    single_start = il(controller + '+FireOperation', 'Start')
    single_update = il(controller + '+FireOperation', 'Update')
    check('Single-fire cooldown is initialized from 60 / SingleFireRate',
          'ldc.r4 60.0' in single_start and '::get_SingleFireRate' in single_start and
          'stfld ' + controller + '+FireOperation::_singleFireCooldownTime' in single_start)
    check('Single-fire Update subtracts elapsed deltaTime from that cooldown',
          bool(re.search(r'::_singleFireCooldownTime[^\n]*\n[^\n]*ldarg.1\n[^\n]*sub\n[^\n]*stfld[^\n]*::_singleFireCooldownTime', single_update)))
    automatic = il(controller + '+AutomaticFireOperation', 'Update')
    check('Automatic operation uses float_7 as elapsed time and _shotsTime as interval',
          bool(re.search(r'::float_7[^\n]*\n[^\n]*ldarg.1\n[^\n]*add\n[^\n]*stfld[^\n]*::float_7', automatic)) and
          '::get_FireRate' in automatic and '::_shotsTime' in automatic and '::OverheatFirerateMult' in automatic)
    check('Projectile CurrentVelocity getter returns _currentVelocity',
          'ldfld EFT.Ballistics.Shot::_currentVelocity' in il('EFT.Ballistics.Shot', 'get_CurrentVelocity'))
    removal = il('EFT.Ballistics.BallisticsCalculator', 'RemovePredicate')
    check('Ballistics removal predicate checks completion and queues the shot for release',
          '::get_IsShotAndParentFinished' in removal and '::_shotsToRelease' in removal and '>::Add' in removal)
    check('Smoke EmitTime getter returns _emitTime and LifeTime scales it by the final curve time',
          'ldfld SmokeGrenade::_emitTime' in il('SmokeGrenade', 'get_EmitTime') and
          all(s in il('SmokeGrenade', 'get_LifeTime') for s in ['::get_EmitTime', 'UnityEngine.Keyframe::get_time', ' mul']))
    cleanup = il('GrenadeEmission', 'ReleaseSoundSource')
    check('Smoke cleanup releases the sound source and clears source/parent fields',
          'BetterSource::Release' in cleanup and 'stfld GrenadeEmission::_source' in cleanup and
          'stfld GrenadeEmission::_parent' in cleanup and cleanup.count('ldnull') >= 3)
    def field(type_name, name, signature):
        tid = game.find('^' + re.escape(type_name) + '$')[0][0]
        return any(row['Name'] == name and game.signature(4, rid) == signature
                   for rid, row in game.members(tid, 4))

    check('Melee owner reflection fields bind to native Player instances',
          field('EFT.Player+ItemHandsController', '_player', 'EFT.Player') and
          field('EFT.MovementContext', '_player', 'EFT.Player'))
    knife = 'EFT.Player+KnifeController'
    kick = il(knife + '+Kick', 'OnFire')
    distance_write = kick.find('stfld EFT.KnifeCollider::MaxDistance')
    fire_callback = kick.find('callvirt EFT.KnifeCollider::OnFire')
    check('Every native knife attack resets reach from the selected stab/slash distance before the patched callback',
          all(value in kick for value in ['::get_SecondaryDistance', '::get_PrimaryDistance']) and
          0 <= distance_write < fire_callback)
    cast = il('EFT.KnifeCollider', 'ManualUpdateClient')
    check('Native knife collision uses MaxDistance for its BoxCast range',
          0 <= cast.find('ldfld EFT.KnifeCollider::MaxDistance') < cast.find('UnityEngine.Physics::BoxCastNonAlloc'))
    melee_speed = il('EFT.Player+BaseKnifeController', 'SetMeleeSpeed')
    check('Native melee-speed setter drives both hand animation and player swing animation',
          'ObjectInHandsAnimator::SetMeleeSpeed' in melee_speed and 'EFT.MovementContext::PlayerAnimatorSetSwingSpeed' in melee_speed)
    jump_entry = il('EFT.JumpPlayerState', 'Enter')
    jump_motion = il('EFT.JumpPlayerState', 'ProcessJumpMovement')
    check('Native jump skill boost is initialized on entry and scales vertical lift velocity',
          '::StrengthBuffJumpHeightInc' in jump_entry and 'stfld EFT.JumpPlayerState::_skilledBoost' in jump_entry and
          bool(re.search(r'::_liftForce[^\n]*\n[^\n]*ldarg.0\n[^\n]*ldfld EFT.JumpPlayerState::_skilledBoost[^\n]*\n[^\n]*call UnityEngine.Vector3::op_Multiply', jump_motion)))
    movement_audio = il('EFT.Player', 'CalculateMovementVolumeDefaultMultiplier')
    check('Movement audio hook targets the native rolloff/footstep multiplier',
          all(value in movement_audio for value in ['::GetCachedRolloff', '::get_AnimatorFootstepSign', '::BaseMaxMovementRolloff']))
    consume = il('Stamina', 'Consume')
    process = il('Stamina', 'Process')
    check('Jump consumption reads the native Consumption Delta and stamina multiplier before subtraction',
          'Physical+Consumption::Delta' in consume and 'Stamina::get_Multiplier' in consume and
          0 <= consume.find('Physical+Consumption::Delta') < consume.find(' sub'))
    check('Sprint update reads per-consumption Delta and keeps restoration on its own path',
          'Physical+Consumption::Delta' in process and '::SelfRestoration' in process and '::get_BuffRestoration' in process)
    item_creation = il('EFT.InventoryLogic.Item', '.ctor')
    check('Native item construction adds corpse-loot protection only when the template Unlootable flag is true',
          bool(re.search(r'ItemTemplate::Unlootable[^\n]*\n[^\n]*brfalse[^\n]*\n', item_creation)) and
          'newobj EFT.InventoryLogic.UnlootableComponent::.ctor' in item_creation)
    vault_speed = il('EFT.MovementContext', 'SetVaultingSpeed')
    check('Native vault-speed hook updates the speed property and the vault animator after weight/height calculations',
          all(value in vault_speed for value in ['::get_VaultingHeight', 'PhysicalBase::WalkOverweight',
              'EFT.MovementContext::set_VaultingSpeed', 'EFT.PlayerAnimator::SetVaultingSpeed']))
    weight = il('Physical', 'OnWeightUpdated')
    deferred_tid = game.find(r'^Deferred`1$')[0][0]
    deferred_getter = [row for rid, row in game.members(deferred_tid, 6) if row['Name'] == 'get_Value'][0]
    check('Native physical weight reads both inventory Deferred caches through the virtual getter',
          all(value in weight for value in ['Physical::_player', 'Inventory::TotalWeight ',
              'Inventory::TotalWeightEliteSkill ', 'Deferred`1<float>::op_Implicit']) and
          'callvirt Compute`1<!0>::get_Value' in il('Deferred`1', 'op_Implicit') and
          bool(deferred_getter['Flags'] & 0x40) and not (deferred_getter['Flags'] & 0x100) and
          'ldfld Deferred`1<!0>::_value' in il('Deferred`1', 'get_Value'))
    check('Native jump entry initializes the timer, motion counter, lift, delay and descent fields used for the extra jump',
          all('stfld EFT.JumpPlayerState::' + value in jump_entry for value in
              ['_stateTime', '_framesWithoutMotion', '_jumpState', '_potentialH', '_timeDescending', '_liftForce', '_liftDelay']) and
          field('EFT.JumpPlayerState', '_hasLastHeight', 'bool') and
          all('EFT.JumpPlayerState::' + value in jump_motion for value in
              ['_stateTime', '_liftForce', '_skilledBoost', '_jumpState']))
    interaction = il('EFT.Interactive.WorldInteractiveObject', 'Interact', 'void (EFT.EInteractionType)')
    check('Native enum Open interaction force-unlocks a locked door before dispatching normal interaction',
          bool(re.search(r'ldc.i4.2\n[^\n]*ldc.i4.1\n[^\n]*call EFT.Interactive.WorldInteractiveObject::SetDoorState', interaction)) and
          '::get_DoorState' in interaction and
          0 <= interaction.find('::SetDoorState') < interaction.find('callvirt EFT.Interactive.WorldInteractiveObject::Interact'))
    find_interactable = il('EFT.GameWorld', 'FindInteractable')
    check('Native interaction probe performs an interaction raycast and an obstruction linecast before returning its target',
          all(value in find_interactable for value in ['PhysicsExtensions::Raycast', '::_interactiveLootMaskWPlayer',
              '::_lootMaskObstruction', 'UnityEngine.Physics::Linecast', 'UnityEngine.RaycastHit::get_point']) and
          0 <= find_interactable.find('PhysicsExtensions::Raycast') < find_interactable.find('UnityEngine.Physics::Linecast'))
    overlap = il('EFT.Player+BaseKnifeController', 'ProcessOverlap')
    check('Native knife overlap hook consumes the active hit callback and processes the same collider as the door hook',
          all(value in overlap for value in ['EFT.KnifeCollider::OnHit', 'System.Delegate::Remove',
              'EFT.Player+KnifeRaycastHit::collider', 'EFT.Player+BaseKnifeController::ProcessHit']))
    manual_update = il('EFT.KnifeCollider', 'ManualUpdate')
    check('Native knife update gates collision on the active OnHit window and chooses client or AI using Owner',
          bool(re.search(r'EFT.KnifeCollider::OnHit[^\n]*\n[^\n]*brfalse', manual_update)) and
          all(value in manual_update for value in ['EFT.KnifeCollider::Owner', 'EFT.Player::get_IsAI',
              '::ManualUpdateServerAI', '::ManualUpdateClient']))
    for operation, process in [('LoadMagazine', 'LoadMagazineProcess'), ('UnloadMagazine', 'UnloadMagazineProcess')]:
        code = il('EFT.Player+PlayerInventoryController+CG_' + operation, 'MoveNext')
        check(operation + ' async body has exactly one native process allocation',
              sum('newobj EFT.Player+PlayerInventoryController+' + process + '::.ctor' in line for line in code.splitlines()) == 1)
    for owner, method in [('EFT.PlayerInputTranslator', 'vmethod_1'), ('EFT.FirearmHandsInputTranslator', 'ReloadExternalMagazine'),
                           ('BotReload', 'GetMagazineForReload'), ('BotReload', 'AddAmmoToMagazines')]:
        code = il(owner, method)
        check(owner + '.' + method + ' has exactly one concrete inventory search', code.count('::GetReachableItemsOfTypeNonAlloc ') == 1)
    check('Ambient coroutine has exactly one final AudioSource volume assignment',
          il('Audio.AmbientSubsystem.BaseAmbientSoundPlayer+CG_PlayCoroutine', 'MoveNext').count('UnityEngine.AudioSource::set_volume ') == 1)
    jump = il('EFT.JumpPlayerState', 'ProcessJumpMovement')
    check('Jump horizontal momentum has the expected adjacent magnitude/multiply hook',
          len(re.findall(r'ldfld EFT.JumpPlayerState::_currentMagnitude[^\n]*\n[^\n]*call UnityEngine.Vector3::op_Multiply', jump)) == 1)
    check('Native medical residue reads the damage-effect duration',
          'ldfld JsonType.DamageEffectSpecification::Duration' in il('EFT.HealthSystem.ActiveHealthController+MedEffect', 'Residue'))
    check('Walking weight limit has exactly one native assignment before combined movement restrictions',
          il('Physical', 'OnWeightUpdated').count('stfld PhysicalBase::WalkSpeedLimit ') == 1)
    extraction = il('CommonAssets.Scripts.Game.EndByExitTrigerScenario', 'Update')
    check('Individual extraction has two time reads and the first belongs to its LocationEscape trigger',
          extraction.count('ldfld EFT.Interactive.ExitTriggerSettings::ExfiltrationTime ') == 2 and
          bool(re.search(r'LocationEscape::Trigger[^\n]*\n[^\n]*Settings[^\n]*\n[^\n]*ExfiltrationTime', extraction)))
    check('Extraction UI has exactly two native countdown reads',
          il('EFT.AbstractGame', 'UpdateExfiltrationUi').count('ldfld EFT.Interactive.ExitTriggerSettings::ExfiltrationTime ') == 2)
    grenade = il('EFT.Player+BaseGrenadeHandsController', 'CreateAndRegisterGrenade')
    check('Rig grenade hook returns a factory grenade registered with the game level',
          0 <= grenade.find('GrenadeFactory::Create ') < grenade.find('EFT.IGameLevel::RegisterGrenade '))
    check('Rig throw completion forwards the native grenade-operation completion event',
          'EFT.Player+IGrenadeOperation::OnDropFinishedAction ' in
          il('EFT.Player+BaseGrenadeHandsController', 'OnDropFinishedAction'))
    check('Rig manual packing observes the per-load native UI sound event',
          il('EFT.Player+PlayerInventoryController+LoadMagazineProcess', 'PlaySound')
          .count('EFT.UI.GUISounds::PlayUILoadSound ') == 1)
    check('Rig medical-cost transpiler has exactly one native medkit resource store',
          il('EFT.HealthSystem.ActiveHealthController+MedEffect', 'RegularUpdate')
          .count('stfld EFT.InventoryLogic.MedKitComponent::HpResource ') == 1)
    med_type = game.find(r'^EFT.HealthSystem.ActiveHealthController\+MedEffect$')[0][0]
    check('Native MedEffect is a public nested type; a NonPublic-only lookup is invalid',
          game.tables[2][med_type]['Flags'] & 7 == 2)
    for method_name in ['Started', 'Residue', 'RegularUpdate']:
        native_method = next(row for rid, row in game.members(med_type, 6) if row['Name'] == method_name)
        check('Native medical lifecycle method is public: ' + method_name,
              native_method['Flags'] & 7 == 6)
    caption = il('EFT.UI.ItemSpecificationPanel', 'method_0')
    inspection = il('EFT.UI.ItemSpecificationPanel', 'method_1')
    check('Remaining obfuscated inspection-caption hook reads ItemName and sets the native caption',
          '::get_ItemName' in caption and '::SetCaptionText' in caption)
    check('Remaining obfuscated inspection-details hook initializes category, description and weight using Examined',
          all(value in inspection for value in ['::get_Examined', '::SetItemTypeText', '::SetDescriptionText', '::SetWeightText']))
    disarm = il('EFT.InteractionContextHelper+CG_GetAvailableActions7', 'method_0')
    check('Remaining obfuscated tripwire callback uses plantTime for the objective, sound and actual Plant action',
          disarm.count('::plantTime') == 3 and all(value in disarm for value in ['::ShowObjectivesPanel', '::PlayTripwireInteractionSound', 'EFT.MovementState::Plant']))
    planting = il('EFT.InventoryLogic.Operations.PlantTripwireOperation', 'ExecuteInternal')
    check('Jigsaw bypass selects the native composite execution after only the human missing-kit guard',
          all(value in planting for value in ['::get_IsAI', '::_plantingKit', 'PlantingKit is not specified', '::ExecuteAndFinish']))
    bolt = il(controller + '+BoltActionFireOperation', 'Start')
    check('Rig bolt hook enters native single-fire operation before configuring bolt-action reload',
          0 <= bolt.find('+FireOperation::Start') < bolt.find('FirearmsAnimator::SetBoltActionReload'))
    utility = il(controller + '+UtilityOperation', 'Start')
    check('Rig utility hook uses the native check-type field initialized by Start and cleared by Reset',
          'stfld ' + controller + '+UtilityOperation::_utilityOperationType' in utility and
          'stfld ' + controller + '+UtilityOperation::_utilityOperationType' in il(controller + '+UtilityOperation', 'Reset'))
    medical_start = il('EFT.HealthSystem.ActiveHealthController+MedEffect', 'Started')
    check('Native MedEffect serves food/drink as well as medical kits, requiring item eligibility checks',
          '::_foodDrink' in medical_start and '::_medKit' in medical_start)
    for operation, method_name in [('LoadMagazineProcess', 'method_5'), ('UnloadMagazineProcess', 'method_4')]:
        cooldown = il('EFT.Player+PlayerInventoryController+' + operation, method_name)
        check('Grenade packing changes the live cooldown read by native Task.Delay: ' + operation,
              '::_cooldown' in cooldown and '::_loadOneAmmoSpeed' not in cooldown and
              0 <= cooldown.find('::_cooldown') < cooldown.find('System.Threading.Tasks.Task::Delay'))
    cook = il('EFT.Player+BaseGrenadeHandsController', 'Cook')
    check('Deferred smoke hook is native Cook: creates emission and attaches it to the held grenade transform',
          all(value in cook for value in ['::GetEmissionEffect', '::GrenadeEmission', '::_grenadeTransform', '::AttachTo', '::StartEmission']))
    check('Throw-animation hook is native StartCountdown and forwards to the grenade operation',
          'EFT.Player+IGrenadeOperation::StartCountdown' in il('EFT.Player+BaseGrenadeHandsController', 'StartCountdown'))
    blast = il('EFT.ExplosionSharedMethods', 'AffectPlayersShared')
    check('Grenade falloff hook targets the native interpolation multiplied by explosive strength',
          bool(re.search(r'Mathf::InverseLerp[^\n]*\n[^\n]*ldarg.1\n[^\n]*get_GetStrength[^\n]*\n[^\n]*mul', blast)))
    flash = il('EFT.FirearmControllerSharedMethods', 'ApplyLightAndSoundHealthEffects')
    check('Flash adapter targets native player-emitter cache processing and native stun/burn effects',
          all(value in flash for value in ['EFT.PlayerEmitterCache', '::DoStun', '::DoBurnEyes']))
    match = il('EFT.Quests.ConditionsConnectorsManager`1', 'ContainsPass')
    check('Quest item matcher is the native identity/category boundary, preserving FindItem category gating',
          all(value in match for value in ['::get_StringTemplateId', 'ConditionFindItem::get_TargetIsCategory', 'Enumerable::Contains']) and 'SpawnedInSession' not in match)
    handover = il('EFT.InventoryLogic.ItemManipulator', 'HandoverQuest')
    check('Final client handover target contains the native discard transaction guarded by the new prefix',
          'EFT.InventoryLogic.ItemManipulator::Discard' in handover)
    commit = il('EFT.InteractionContextHelper+CG_GetAvailableActions5', 'method_0')
    check('Placement completion reports success and closes the panel before the guarded removal path',
          0 <= commit.find('::PlantItemNetwork') < commit.find('::CloseObjectivesPanel') < commit.find('brfalse') < commit.find('ItemManipulator::Remove'))
    plant = il('EFT.Player', 'PlantItem')
    check('Native placement success flag gates quest progress before ItemDroppedAtPlace',
          bool(re.search(r'ldarg.3\n[^\n]*brfalse', plant)) and plant.find('brfalse') < plant.find('::ItemDroppedAtPlace'))
    check('Native placement network bridge forwards the same success flag into PlantItem',
          'ldarg.3' in il('EFT.Player', 'PlantItemNetwork') and 'EFT.Player::PlantItem ' in il('EFT.Player', 'PlantItemNetwork'))
    check('Grenade inspection binds attributes before native compact/full row construction',
          all(value in il('EFT.UI.ItemSpecificationPanel', 'RecreateAttributeBars') for value in ['::_item', 'EFT.InventoryLogic.Item::Attributes']))
    check('Grenade comparison targets the native method reading the compared item attributes',
          'EFT.InventoryLogic.Item::Attributes' in il('EFT.UI.ItemSpecificationPanel', 'Compare'))
    gear = il('EFT.Player', 'PlayGearSound', 'void (EFT.SoundBank, float)')
    check('Headset gear scope targets the native SoundBank/volume overload using the gear source',
          '::_gearSource' in gear and 'EFT.Player::SetRolloff' in gear)
    source_rolloff = il('EFT.Player', 'SetRolloff')
    check('Actor audio scope surrounds native environment/hearing scaling before BetterSource.SetRolloff',
          all(value in source_rolloff for value in ['::GetRolloffMultByEnvironment', '::get_ProtagonistHearing', 'BetterSource::SetRolloff']))
    check('Secondary footstep transfer has exactly 1 primary-source range read',
          il('EFT.Player', 'UpdateStepSourceRolloff').count('BetterSource::get_MaxDistance') == 1)
    for source_type, volume_field in [('BetterSource','PreOcclusionVolume'), ('SuperSource','_secondPreEffectsVolume'), ('SuperSourceDistant','_secondPreEffectsVolume')]:
        body = il(source_type, 'UpdateSourceVolume')
        check('Headset output scaling reads the native cached channel exactly once: ' + source_type,
              body.count('ldfld ' + source_type + '::' + volume_field + ' ') == 1)
    mixer = il('EFT.ActiveHeadphones.ActiveHeadphonesDefaultConfigurator', 'Configure')
    check('Native headset configuration exposes separate gun buses and ambient/environment levels',
          all(value in mixer for value in ["'GunsVolume'", "'GunsCompressorSendLevel'", "'EnvironmentVolume'", "'AmbientVolume'", '::SetMixerFloat']))
    check('Native gun queue invokes the concrete SuperAudioSample playback boundary',
          'SuperAudioSample::PlayOn' in il('SuperBetterAudioQueue', 'Update'))
    sample = il('SuperAudioSample', 'PlayOn')
    check('Gun sample writes channel volumes before scheduled playback, requiring a post-playback refresh',
          0 <= sample.find('::set_volume') < sample.find('BetterSource::PlayScheduled'))
    recoil = il('EFT.Animations.NewRotationRecoilProcess', 'CalculateRecoil')
    check('The renamed automatic recoil cache feeds the real return vector calculation',
          '::_autoFireSpeed' in recoil and '::AutoFireOn' in recoil and 'Vector3::op_Multiply' in recoil)
    residue = il('EFT.HealthSystem.ActiveHealthController+MedEffect', 'ForceResidue')
    check('Interrupted food uses set the native flag tested by the completion adapter',
          bool(re.search(r'ldc.i4.1\n[^\n]*stfld[^\n]*::_interrupted', residue)) and '::_interrupted' in il('EFT.HealthSystem.ActiveHealthController+MedEffect', 'Residue'))
    check('Tinnitus duration hook preserves the native worn-headphone check and tinnitus dispatch',
          all(value in il('EFT.Player', 'TryStartContusion') for value in ['EFT.InventoryLogic.Headphones', '::StartTinnitusEffect']))
    check('Malfunction-cleared trigger targets the completed repair event before returning to idle',
          all(value in il('EFT.Player+FirearmController+RepairMalfunction', 'OnMalfunctionOffEvent') for value in ['::CallMalfunctionRepaired', '::SwitchToIdle']))
    visual = il('EFT.Grenade', 'Explosion', 'void ()')
    check('The instance grenade Explosion dispatches one visual before its attributed blast',
          visual.count('Effects::EmitGrenade') == 1 and 0 <= visual.find('Effects::EmitGrenade') < visual.find('EFT.Grenade::Explosion'))
    sampler = il('ParticleSystemAdapter', 'Emit', 'void (UnityEngine.Vector3, UnityEngine.Vector3, float)')
    check('Explosion envelope buffers drive native lifetime, velocity and size emission',
          all(value in sampler for value in ['::_lifeTimes', '::_speeds', '::_sizes', '::set_startLifetime', '::set_velocity', '::set_startSize']))
    mediator = il('EFT.Particles.BasicParticleSystemMediator', 'Emit')
    check('Native basic-particle reuse selects the current index and advances it after playback',
          0 <= mediator.find('::_index') < mediator.find('ParticleSystem::Play') < mediator.find('stfld EFT.Particles.BasicParticleSystemMediator::_index'))
    secondary = il('Audio.AmbientSubsystem.PlayerStepLayerAudioController', 'Play')
    check('The reflected secondary footstep source is passed to native SoundBank playback',
          0 <= secondary.find('::_source') < secondary.find('EFT.SoundBank::Play'))
    barrels = il(controller, 'ReloadBarrels')
    check('Native barrel reload validates blindfire, reload eligibility and ammo before forwarding callbacks',
          all(value in barrels for value in ['::get_Blindfire', '::CanStartReload', '::get_AmmoCount', 'FirearmOperation::ReloadBarrels', 'CallbackExtensions::Fail']))
    autopan = il('EFT.UI.UIExtensions', 'KeepChildInScrollViewPort')
    check('Native inventory auto-pan updates both axes and translates the content after layout',
          all(value in autopan for value in ['Canvas::ForceUpdateCanvases', '::set_horizontalNormalizedPosition', '::set_verticalNormalizedPosition', '::set_localPosition']))
    check('Native deferred effect update has exactly one queued Effect.Emit dispatch',
          il('Systems.Effects.Effects', 'Update').count('Systems.Effects.Effects+Effect::Emit ') == 1)
    core = Assembly(args.references / 'Server/SPTarkov.Server.Core.dll')
    airdrop = 'SPTarkov.Server.Core.Services.InRaid.AirdropService'
    generation = il(airdrop, 'GenerateAirdropLoot', assembly=core)
    config_at = generation.find('::GetAirdropLootConfigByType')
    random_at = generation.find('LootGenerator::CreateRandomLoot')
    forced_at = generation.find('LootGenerator::CreateForcedLoot')
    fit_at = generation.find('::GetLootThatFitsContainer')
    check('Native airdrop config filtering precedes both random/forced generation and fitting',
          0 <= config_at < random_at < fit_at and config_at < forced_at < fit_at)
    check('Custom native airdrops forward both mapped and fallback requests through shared generation',
          il(airdrop, 'GenerateCustomAirdropLoot', assembly=core).count(airdrop + '::GenerateAirdropLoot ') == 2)
    fit = il(airdrop, 'GetLootThatFitsContainer', assembly=core)
    check('Native crate fitting reads generated item size before slot selection and map fill',
          0 <= fit.find('ItemHelper::GetItemSize') < fit.find('ContainerExtensions::FindSlotForItem') < fit.find('ContainerExtensions::TryFillContainerMapWithItem'))
    report = {'version': '0.30.0', 'targetSptVersion': '4.1.6',
              'scope': 'Native method-body role checks; no game execution',
              'passed': sum(c['status'] == 'PASS' for c in checks),
              'failed': sum(c['status'] == 'FAIL' for c in checks), 'checks': checks}
    args.output.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))
    raise SystemExit(bool(report['failed']))


if __name__ == '__main__':
    main()
