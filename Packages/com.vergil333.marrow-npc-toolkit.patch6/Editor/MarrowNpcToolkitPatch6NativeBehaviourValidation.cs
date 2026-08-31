using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;

namespace Vergil333.MarrowNpcToolkit.ProjectCompatibility
{
    /// <summary>
    /// Saved-prefab semantic guard for the Patch 6 behaviour milestone.  The
    /// coordinator already fingerprints every serialized property; this guard
    /// adds the relationships and invariants that a byte-complete snapshot
    /// cannot understand (for example, which foot a locomotion reference owns).
    /// </summary>
    internal static class MarrowNpcToolkitPatch6NativeBehaviourValidation
    {
        private const float Epsilon = 0.0001f;
        private const string BeingTag = "SLZ.Marrow.BoneTag.Being";
        private const string BloodSurface = "SLZ.Backlot.SurfaceDataCard.Blood";
        private const int BloodColliderDecalType = -1;

        private static readonly HumanBodyBones[] EntityOrder =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Spine,
            HumanBodyBones.Chest,
            HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot,
        };

        private static readonly HumanBodyBones[] EntityOrderWithJaw =
            EntityOrder.Concat(new[] { HumanBodyBones.Jaw }).ToArray();

        private static readonly IReadOnlyDictionary<HumanBodyBones, int>
            SensorIndices = new Dictionary<HumanBodyBones, int>
            {
                [HumanBodyBones.Hips] = 0,
                [HumanBodyBones.LeftUpperLeg] = 1,
                [HumanBodyBones.LeftFoot] = 3,
                [HumanBodyBones.RightUpperLeg] = 4,
                [HumanBodyBones.RightFoot] = 6,
                [HumanBodyBones.Chest] = 8,
            };

        private static readonly int[] HealthGroups =
        {
            0, 3, 3, 3, 4, 4, 4, 0, 0, 5, 1, 1, 1, 2, 2, 2,
        };

        private static readonly int[] HealthGroupsWithJaw =
            HealthGroups.Concat(new[] { 5 }).ToArray();

        internal static string ValidateAndFingerprint(
            GameObject outputRoot,
            NpcDefinition definition,
            Transform animationRoot,
            Transform physicsRoot)
        {
            return Hash128.Compute(ValidateAndReceipt(
                outputRoot, definition, animationRoot, physicsRoot)).ToString();
        }

        internal static string ValidateAndReceipt(
            GameObject outputRoot,
            NpcDefinition definition,
            Transform animationRoot,
            Transform physicsRoot)
        {
            var validator = new Validator(
                outputRoot, definition, animationRoot, physicsRoot);
            validator.Validate();
            return validator.Fingerprint();
        }

        internal static void ValidateConfiguredAnimatorController(
            RuntimeAnimatorController runtimeController)
        {
            if (runtimeController == null)
                throw new InvalidOperationException(
                    "The configured Animator Controller is missing.");
            // Controller inspection needs no prefab graph. Reuse the exact
            // saved-build contract so readiness and post-save validation cannot
            // drift into accepting different state/parameter requirements.
            var validator = new Validator(null, null, null, null);
            validator.ValidateController(runtimeController);
        }

        private sealed class Validator
        {
            private readonly GameObject _root;
            private readonly NpcDefinition _definition;
            private readonly Transform _animationRoot;
            private readonly Transform _physicsRoot;
            private readonly Dictionary<HumanBodyBones, Rigidbody> _bodies;
            private readonly Dictionary<HumanBodyBones, Component> _marrowBodies;
            private readonly Dictionary<HumanBodyBones, Collider> _primaryColliders;
            private readonly List<Component> _fingerprintedComponents =
                new List<Component>();
            private readonly List<UnityEngine.Object> _fingerprintedAssets =
                new List<UnityEngine.Object>();
            private Patch6MovementBuildSettings _movement;

            private Type _entityType;
            private Type _marrowBodyType;
            private Type _puppetType;
            private Type _pooleeType;
            private Type _brainType;
            private Type _powerType;
            private Type _hostType;
            private Type _hostManagerType;
            private Type _trackerType;
            private Type _proxyType;
            private Type _sensorType;
            private Type _liteLocoType;
            private Type _agentLinkType;
            private Type _navAgentType;
            private Type _visualDamageType;
            private Type _impactPropertiesType;
            private Type _damageReceiverType;
            private Type _rebindType;
            private Type _limbIkType;

            private Component _entity;
            private Component _puppet;
            private Component _poolee;
            private Component _brain;
            private Component _power;
            private Component _hostManager;
            private Component _proxy;
            private Component _agentLink;
            private Component _liteLoco;
            private Component _navAgent;
            private Component _visualDamage;
            private Transform _aiRig;
            private Transform _powerRoot;
            private Transform _eye;
            private AudioSource _impactSource;
            private Animator _animator;

            private IReadOnlyList<HumanBodyBones> OrderedRoles =>
                _definition != null && _definition.IncludePhysicalJaw
                    ? EntityOrderWithJaw
                    : EntityOrder;

            private IReadOnlyList<int> OrderedHealthGroups =>
                _definition != null && _definition.IncludePhysicalJaw
                    ? HealthGroupsWithJaw
                    : HealthGroups;

            internal Validator(
                GameObject root,
                NpcDefinition definition,
                Transform animationRoot,
                Transform physicsRoot)
            {
                _root = root;
                _definition = definition;
                _animationRoot = animationRoot;
                _physicsRoot = physicsRoot;
                _bodies = new Dictionary<HumanBodyBones, Rigidbody>();
                _marrowBodies = new Dictionary<HumanBodyBones, Component>();
                _primaryColliders = new Dictionary<HumanBodyBones, Collider>();
            }

            internal void Validate()
            {
                ValidateArguments();
                ResolveTypes();
                ResolveGraph();
                ValidateAnimator();
                ValidatePoolingAndBodyCaches();
                ValidateHostsAndTrackers();
                ValidateProxyAndSensors();
                ValidateConfiguredProvenance();
                ValidateBrainPowerAndHealth();
                ValidateLimbIk();
                ValidateSkinnedBoneRebind();
                ValidateAgentLink();
                ValidateLocomotionAndNavigation();
                ValidateVisualDamage();
            }

            internal string Fingerprint()
            {
                var text = new StringBuilder(65536);
                text.Append("patch6-native-behaviour-v2|")
                    .Append(ObjectKey(_animationRoot)).Append('|')
                    .Append(ObjectKey(_physicsRoot)).Append('|');

                foreach (Component component in _fingerprintedComponents
                             .Where(value => value != null)
                             .Distinct()
                             .OrderBy(ObjectKey, StringComparer.Ordinal))
                    AppendSerializedObject(text, component);

                foreach (UnityEngine.Object asset in _fingerprintedAssets
                             .Where(value => value != null)
                             .Distinct()
                             .OrderBy(ObjectKey, StringComparer.Ordinal))
                    text.Append("asset=").Append(ObjectKey(asset)).Append('|');

                if (_movement != null)
                    text.Append("movementRecipe=")
                        .Append(_movement.ProviderRecipeFingerprint)
                        .Append("|movementAuthoring=")
                        .Append(_movement.Profile == null
                            ? "legacy"
                            : _movement.Profile.AutoFitAuthoringFingerprint)
                        .Append("|movementProfile=")
                        .Append(_movement.Profile == null
                            ? "legacy"
                            : ObjectKey(_movement.Profile))
                        .Append('|');

                // Return the canonical receipt rather than pre-hashing it.
                // The coordinator hashes the complete provider receipt into
                // the public output fingerprint. Keeping these stable tokens
                // here lets a rejected save/reload report the first exact
                // native field that changed instead of only two opaque hashes.
                return text.ToString();
            }

            private void ValidateArguments()
            {
                if (_root == null || _definition == null
                    || _animationRoot == null || _physicsRoot == null)
                    Fail("The behaviour validator requires the output root, NPC "
                         + "Definition, AnimationRoot, and Physics root.");
                if (_definition.AnatomyProfile == null
                    || _definition.AvatarSourceProfile == null)
                    Fail("The NPC Definition has no accepted anatomy/source profile.");
                if (_animationRoot.parent != _root.transform
                    || _physicsRoot.parent != _root.transform)
                    Fail("AnimationRoot and Physics must be direct output-root siblings.");
            }

            private void ResolveTypes()
            {
                _entityType = Resolve("SLZ.Marrow.Interaction.MarrowEntity", "SLZ.Marrow");
                _marrowBodyType = Resolve("SLZ.Marrow.Interaction.MarrowBody", "SLZ.Marrow");
                _puppetType = Resolve("SLZ.Marrow.PuppetMasta.PuppetMaster", "SLZ.Marrow");
                _pooleeType = Resolve("SLZ.Marrow.Pool.Poolee", "SLZ.Marrow");
                _brainType = Resolve("SLZ.Marrow.AI.AIBrain", "SLZ.Marrow");
                _powerType = Resolve("PuppetMasta.BehaviourPowerLegs", "Assembly-CSharp");
                _hostType = Resolve("SLZ.Marrow.InteractableHost", "SLZ.Marrow");
                _hostManagerType = Resolve(
                    "SLZ.Marrow.InteractableHostManager", "SLZ.Marrow");
                _trackerType = Resolve("SLZ.Marrow.Interaction.Tracker", "SLZ.Marrow");
                _proxyType = Resolve("SLZ.Marrow.AI.TriggerRefProxy", "SLZ.Marrow");
                _sensorType = Resolve(
                    "SLZ.Marrow.PuppetMasta.MuscleCollisionBroadcasterSensor",
                    "SLZ.Marrow");
                _liteLocoType = Resolve("SLZ.Marrow.Mechanics.LiteLoco", "SLZ.Marrow");
                _agentLinkType = Resolve("SLZ.Bonelab.AgentLinkControl", "Assembly-CSharp");
                _navAgentType = Resolve("UnityEngine.AI.NavMeshAgent", "UnityEngine.AIModule");
                _visualDamageType = Resolve(
                    "SLZ.Marrow.Combat.VisualDamageController", "SLZ.Marrow");
                _impactPropertiesType = Resolve(
                    "SLZ.Marrow.ImpactProperties", "SLZ.Marrow");
                _damageReceiverType = Resolve(
                    "SLZ.Combat.VisualDamageReceiver", "Assembly-CSharp");
                _rebindType = Resolve(
                    "SLZ.Marrow.PuppetMasta.SkinnedBoneRebind", "SLZ.Marrow");
                _limbIkType = Resolve("SLZ.VRMK.LimbIKSlz", "Assembly-CSharp");
            }

            private void ResolveGraph()
            {
                _entity = RequireSingletonAt(_root.transform, _entityType, "MarrowEntity");
                _puppet = RequireSingletonAt(_physicsRoot, _puppetType, "PuppetMaster");
                _poolee = RequireSingletonAt(_root.transform, _pooleeType, "Poolee");
                _brain = RequireSingletonAt(_root.transform, _brainType, "AIBrain");
                _hostManager = RequireSingletonAt(
                    _root.transform, _hostManagerType, "InteractableHostManager");
                _agentLink = RequireSingletonAt(
                    _root.transform, _agentLinkType, "AgentLinkControl");
                _visualDamage = RequireSingletonAt(
                    _root.transform, _visualDamageType, "VisualDamageController");

                _aiRig = DirectChild(_root.transform, "AiRig");
                _powerRoot = DirectChild(_aiRig, "BehaviourPowerLegs");
                _liteLoco = RequireSingletonAt(_aiRig, _liteLocoType, "LiteLoco");
                _navAgent = RequireSingletonAt(_aiRig, _navAgentType, "NavMeshAgent");
                _power = RequireSingletonAt(
                    _powerRoot, _powerType, "BehaviourPowerLegs");

                Transform impact = DirectChild(_powerRoot, "ImpactSrc");
                AudioSource[] impactSources = impact.GetComponents<AudioSource>();
                if (impactSources.Length != 1)
                    Fail("BehaviourPowerLegs/ImpactSrc must have exactly one AudioSource.");
                _impactSource = impactSources[0];

                foreach (HumanBodyBones role in OrderedRoles)
                {
                    Transform body = UniqueNamedDescendant(_physicsRoot, role.ToString());
                    Rigidbody rigidbody = RequireOnly(body.GetComponents<Rigidbody>(),
                        role + " Rigidbody");
                    Component marrowBody = RequireOnly(
                        body.GetComponents(_marrowBodyType).Cast<Component>().ToArray(),
                        role + " MarrowBody");
                    Collider[] owned = body.GetComponentsInChildren<Collider>(true)
                        .Where(value => OwningRigidbody(value.transform) == rigidbody
                                        && value.GetComponent(_trackerType) == null
                                        && !value.name.StartsWith("Tracker[",
                                            StringComparison.Ordinal))
                        .ToArray();
                    // The core provider owns exactly one physical collider.  Entity
                    // tracker boxes are children of the same rigidbody, so exclude
                    // their well-defined tracker objects above.
                    Collider[] physical = owned.Where(value =>
                            value.transform.GetComponent(_trackerType) == null
                            && !value.transform.name.StartsWith("Tracker[",
                                StringComparison.Ordinal))
                        .ToArray();
                    if (physical.Length != 1)
                        Fail(role + " must retain exactly one primary physical collider.");
                    _bodies.Add(role, rigidbody);
                    _marrowBodies.Add(role, marrowBody);
                    _primaryColliders.Add(role, physical[0]);
                }

                if (_root.GetComponentsInChildren(_entityType, true).Length != 1
                    || _root.GetComponentsInChildren(_puppetType, true).Length != 1
                    || _root.GetComponentsInChildren(_pooleeType, true).Length != 1
                    || _root.GetComponentsInChildren(_brainType, true).Length != 1
                    || _root.GetComponentsInChildren(_powerType, true).Length != 1
                    || _root.GetComponentsInChildren(_hostManagerType, true).Length != 1
                    || _root.GetComponentsInChildren(_agentLinkType, true).Length != 1
                    || _root.GetComponentsInChildren(_liteLocoType, true).Length != 1
                    || _root.GetComponentsInChildren(_navAgentType, true).Length != 1)
                    Fail("The native root contains duplicate AI, pooling, host, or "
                         + "locomotion controller components.");

                Track(_entity, _puppet, _poolee, _brain, _hostManager, _agentLink,
                    _visualDamage, _liteLoco, _navAgent, _power, _impactSource);
            }

            private void ValidateAnimator()
            {
                Animator[] animators = _animationRoot.GetComponentsInChildren<Animator>(true);
                if (animators.Length != 1)
                    Fail("AnimationRoot must contain exactly one Animator; found "
                         + animators.Length + ".");
                _animator = animators[0];
                if (!_animator.enabled || _animator.avatar == null
                    || !_animator.avatar.isHuman
                    || _animator.runtimeAnimatorController == null
                    || _animator.applyRootMotion
                    || _animator.cullingMode != AnimatorCullingMode.AlwaysAnimate
                    || _animator.updateMode != AnimatorUpdateMode.Normal)
                    Fail("The routed Animator must be enabled, Humanoid, controller-backed, "
                         + "Always Animate/Normal, and have Apply Root Motion disabled.");
                if (Obj(_puppet, "targetRoot") != _animationRoot)
                    Fail("PuppetMaster.targetRoot is not the direct AnimationRoot.");
                Track(_animator);
                TrackAsset(_animator.avatar, _animator.runtimeAnimatorController);
                ValidateController(_animator.runtimeAnimatorController);
            }

            private void ValidatePoolingAndBodyCaches()
            {
                if (Obj(_entity, "_poolee") != _poolee
                    || Obj(_puppet, "_poolee") != _poolee
                    || Obj(_brain, "_poolee") != _poolee
                    || Obj(_power, "_poolee") != _poolee)
                    Fail("Entity, PuppetMaster, AIBrain, and PowerLegs must share "
                         + "the one root Poolee.");

                float totalMass = 0f;
                foreach (HumanBodyBones role in OrderedRoles)
                {
                    Rigidbody body = _bodies[role];
                    totalMass += body.mass;
                    var serialized = new SerializedObject(_marrowBodies[role]);
                    SerializedProperty info = Prop(serialized, "_defaultRigidbodyInfo");
                    Vector3 center = Rel(info, "centerOfMass").vector3Value;
                    Vector3 inertia = Rel(info, "inertiaTensor").vector3Value;
                    Quaternion inertiaRotation = Rel(
                        info, "inertiaTensorRotation").quaternionValue;
                    if (!Finite(center) || !Finite(inertia)
                        || inertia.x <= 0f || inertia.y <= 0f || inertia.z <= 0f
                        || !Normalized(inertiaRotation))
                        Fail(role + " has invalid default center-of-mass/inertia cache.");

                    SerializedProperty spawn = Prop(
                        serialized, "<InitInEntityTransform>k__BackingField");
                    Vector3 position = Rel(spawn, "position").vector3Value;
                    Quaternion rotation = Rel(spawn, "rotation").quaternionValue;
                    Vector3 expectedPosition = _entity.transform.InverseTransformPoint(
                        body.transform.position);
                    Quaternion expectedRotation = Quaternion.Inverse(
                        _entity.transform.rotation) * body.transform.rotation;
                    if (!Near(position, expectedPosition, 0.0002f)
                        || !Near(rotation, expectedRotation, 0.0002f))
                        Fail(role + " has a stale entity-relative pool spawn pose.");
                    Track(_marrowBodies[role]);
                }
                if (!Finite(totalMass) || totalMass <= 0f)
                    Fail("The " + OrderedRoles.Count
                         + "-body native graph has no valid total mass.");
                if (_definition.IncludePhysicalJaw
                    && !Near(totalMass, 65f, 0.001f))
                    Fail("The accepted 17-body Physical Jaw graph must total 65 kg.");
            }

            private void ValidateHostsAndTrackers()
            {
                Component[] allHosts = _root.GetComponentsInChildren(_hostType, true)
                    .Cast<Component>().ToArray();
                Component[] allTrackers = _root.GetComponentsInChildren(_trackerType, true)
                    .Cast<Component>().ToArray();
                int expectedCount = OrderedRoles.Count;
                if (allHosts.Length != expectedCount
                    || allTrackers.Length != expectedCount)
                    Fail("The native graph must contain exactly " + expectedCount
                         + " hosts and " + expectedCount + " trackers.");

                SerializedProperty managerHosts = Arr(
                    _hostManager, "hosts", expectedCount);
                Arr(_hostManager, "grabbedHosts", 0);
                SerializedProperty behaviours = Arr(
                    _entity, "_behaviours", expectedCount + 1);
                if (behaviours.GetArrayElementAtIndex(0).objectReferenceValue != _puppet)
                    Fail("MarrowEntity behaviour slot zero must be PuppetMaster.");

                for (int index = 0; index < OrderedRoles.Count; index++)
                {
                    HumanBodyBones role = OrderedRoles[index];
                    Transform body = _bodies[role].transform;
                    Component host = RequireOnly(
                        body.GetComponents(_hostType).Cast<Component>().ToArray(),
                        role + " InteractableHost");
                    if (managerHosts.GetArrayElementAtIndex(index).objectReferenceValue != host
                        || behaviours.GetArrayElementAtIndex(index + 1)
                            .objectReferenceValue != host
                        || Obj(host, "marrowEntity") != _entity
                        || Obj(host, "manager") != null
                        || Int(host, "ignoreBodyOnGrab") != 0
                        || Int(host, "<IsStatic>k__BackingField") != 0)
                        Fail(role + " has an invalid host/entity registry contract.");

                    SerializedProperty controller = P(
                        host, "<VirtualController>k__BackingField");
                    SerializedProperty defaults = Rel(controller, "defaultSettings");
                    Exact(defaults, "lookRotationWeight", 1f, role + " host");
                    Exact(defaults, "handTwistWeight", 0.5f, role + " host");
                    Exact(defaults, "handSwingWeight", 1f, role + " host");
                    Exact(defaults, "positionWeight", 0.5f, role + " host");
                    Exact(defaults, "jointSwingLimit", 90f, role + " host");
                    Exact(defaults, "jointTwistLimit", 90f, role + " host");
                    if (Rel(defaults, "autoTargetUpdatePrimary").boolValue
                        || Rel(defaults, "dynamicHandDistanceWeights").boolValue)
                        Fail(role + " host uses non-baseline controller switches.");

                    Transform trackerRoot = DirectChild(
                        body, role == HumanBodyBones.Jaw
                            ? "Tracker[Jaw_M] Entity"
                            : "Tracker[" + role + "] Entity");
                    Component tracker = RequireOnly(
                        trackerRoot.GetComponents(_trackerType).Cast<Component>().ToArray(),
                        role + " Tracker");
                    BoxCollider trackerBox = RequireOnly(
                        trackerRoot.GetComponents<BoxCollider>(), role + " tracker box");
                    if (body.gameObject.layer != 12 || trackerRoot.gameObject.layer != 26
                        || trackerBox.isTrigger || !trackerBox.enabled
                        || !Finite(trackerBox.center) || !Finite(trackerBox.size)
                        || trackerBox.size.x <= 0f || trackerBox.size.y <= 0f
                        || trackerBox.size.z <= 0f
                        || Obj(tracker, "_entity") != _entity
                        || Obj(tracker, "_body") != _marrowBodies[role]
                        || Obj(tracker, "_collider") != _primaryColliders[role])
                        Fail(role + " has an invalid Entity tracker contract.");
                    SerializedProperty bodyTrackers = Arr(
                        _marrowBodies[role], "_trackers", 1);
                    if (bodyTrackers.GetArrayElementAtIndex(0).objectReferenceValue
                        != tracker)
                        Fail(role + " MarrowBody does not register its Entity tracker.");
                    Track(host, tracker, trackerBox);
                }

                SerializedProperty tags = P(_entity, "_tags._tags");
                if (!tags.isArray || tags.arraySize != 1)
                    Fail("MarrowEntity must contain exactly the Being bone tag.");
                string barcode = Rel(
                    Rel(tags.GetArrayElementAtIndex(0), "_barcode"), "_id").stringValue;
                if (!string.Equals(barcode, BeingTag, StringComparison.Ordinal))
                    Fail("MarrowEntity does not carry the Patch 6 Being bone tag.");
            }

            private void ValidateProxyAndSensors()
            {
                Transform head = _bodies[HumanBodyBones.Head].transform;
                _proxy = RequireOnly(
                    head.GetComponents(_proxyType).Cast<Component>().ToArray(),
                    "Head TriggerRefProxy");
                if (_root.GetComponentsInChildren(_proxyType, true).Length != 1
                    || Int(_proxy, "triggerType") != 2
                    || Int(_proxy, "npcType") != 1
                    || Int(_proxy, "teamNumber") != 0
                    || Obj(_proxy, "root") != _root
                    || Obj(_proxy, "targetHead") != _bodies[HumanBodyBones.Head]
                    || Obj(_proxy, "lfHandRb") != _bodies[HumanBodyBones.LeftHand]
                    || Obj(_proxy, "rtHandRb") != _bodies[HumanBodyBones.RightHand]
                    || Obj(_proxy, "chestTran")
                        != _bodies[HumanBodyBones.Chest].transform
                    || Obj(_proxy, "feetTran")
                        != _bodies[HumanBodyBones.LeftFoot].transform)
                    Fail("Head TriggerRefProxy is not the exact NPC self-proxy.");
                Transform legacy = Obj(_proxy, "legacyProxy") as Transform;
                if (legacy == null || legacy.parent != _root.transform
                    || !string.Equals(legacy.name, "Legacy_Proxy", StringComparison.Ordinal))
                    Fail("Head TriggerRefProxy must use the direct Legacy_Proxy helper.");

                float totalMass = OrderedRoles.Sum(role => _bodies[role].mass);
                var sensors = new Dictionary<int, Component>();
                Component[] allSensors = _root.GetComponentsInChildren(_sensorType, true)
                    .Cast<Component>().ToArray();
                if (allSensors.Length != 6)
                    Fail("The native graph must contain exactly six balance sensors.");
                foreach (KeyValuePair<HumanBodyBones, int> pair in SensorIndices)
                {
                    Component sensor = RequireOnly(
                        _bodies[pair.Key].GetComponents(_sensorType)
                            .Cast<Component>().ToArray(),
                        pair.Key + " balance sensor");
                    if (Obj(sensor, "puppetMaster") != _puppet
                        || Int(sensor, "muscleIndex") != pair.Value
                        || Int(sensor, "isGrounded") != 0
                        || !Near(P(sensor, "groundNormal").vector3Value, Vector3.zero)
                        || !Near(P(sensor, "_totalImpulse").vector3Value, Vector3.zero)
                        || !Near(Float(sensor, "totalMass"), totalMass, 0.001f)
                        || !Near(Float(sensor, "additionalMass"), 0f))
                        Fail(pair.Key + " balance sensor has stale runtime/default data.");
                    sensors.Add(pair.Value, sensor);
                    Track(sensor);
                }

                ValidateReferences(P(_power, "sensors.forceSensorsFeet"),
                    sensors[3], sensors[6]);
                ValidateReferences(P(_power, "sensors.forceSensorsHands"));
                ValidateReferences(P(_power, "sensors.forceSensorsBody"),
                    sensors[0], sensors[1], sensors[4], sensors[8]);
                if (Obj(_power, "sensors.selfTrp") != _proxy
                    || Obj(_power, "sensors.target") != null
                    || Int(_power, "sensors.blockVisionRaycast.m_Bits") != 65
                    || !Near(Float(_power, "sensors.visionFov"), 85f)
                    || !Near(Float(_power, "sensors.additionalMass"), 0f)
                    || !Near(Float(_power, "sensors.footSupported"), 0f)
                    || !Near(Float(_power, "sensors.handSupported"), 0f)
                    || !Near(Float(_power, "sensors.bodySupported"), 0f))
                    Fail("PowerLegs sensor routing/defaults differ from the Patch 6 contract.");
                Track(_proxy);
            }

            private void ValidateBrainPowerAndHealth()
            {
                // The hostility contract is owned by the resolved movement
                // recipe. Keep this validator robust when a staged provider
                // invokes the saved-prefab checks before the normal provenance
                // pass has populated the cached settings.
                if (_movement == null)
                    ValidateConfiguredProvenance();
                if (_movement == null)
                    Fail("PowerLegs hostility validation requires a resolved "
                         + "movement recipe.");

                if (Obj(_brain, "behaviour") != _power
                    || Obj(_brain, "puppetMaster") != _puppet
                    || Int(_brain, "dontClearBaseConfig") != 1
                    || Int(_brain, "isDead") != 0
                    || Obj(_power, "puppetMaster") != _puppet
                    || Obj(_power, "hostManager") != _hostManager)
                    Fail("AIBrain and PowerLegs do not form the required controller cycle.");

                _eye = DirectChild(
                    _bodies[HumanBodyBones.Head].transform, "EyeTran");
                if (_eye.GetComponents<Component>().Length != 1
                    || Vector3.Dot(_eye.forward, _root.transform.forward) < 0.999f
                    || Obj(_power, "eyeTran") != _eye)
                    Fail("Physical Head/EyeTran must be component-free, face prefab +Z, "
                         + "and drive PowerLegs vision.");

                SphereCollider[] spheres = _powerRoot.GetComponents<SphereCollider>();
                if (_powerRoot.gameObject.layer != 30
                    || spheres.Length != 1
                    || spheres[0].gameObject.layer != 30
                    || !spheres[0].enabled || !spheres[0].isTrigger
                    || !Near(spheres[0].radius, 5f)
                    || !Near(spheres[0].center, new Vector3(0f, 0f, 4f)))
                    Fail("BehaviourPowerLegs must own the layer-30 5 m forward "
                         + "vision trigger.");
                Track(spheres[0]);

                UnityEngine.Object config = Obj(_power, "prefabConfig");
                UnityEngine.Object overrideConfig = Obj(_power, "overrideConfig");
                UnityEngine.Object standing = Obj(_power, "standingIdle");
                if (config == null || overrideConfig != config || standing == null)
                    Fail("PowerLegs must use one persistent config and one standing pose.");
                RequirePersistent(config, "Base Enemy Config");
                RequirePersistent(standing, "Standing Pose");
                SerializedProperty posePositions = P(standing, "posePositions");
                SerializedProperty poseRotations = P(standing, "poseRotations");
                int expectedPoseCount = OrderedRoles.Count;
                if (!posePositions.isArray || !poseRotations.isArray
                    || posePositions.arraySize != expectedPoseCount
                    || poseRotations.arraySize != expectedPoseCount)
                    Fail("The standing pose must contain exactly "
                         + expectedPoseCount + " positions/rotations.");
                ValidateStandingPose(standing, posePositions, poseRotations);

                SerializedProperty health = P(_power, "health.muscles");
                if (!health.isArray
                    || health.arraySize != OrderedHealthGroups.Count)
                    Fail("PowerLegs health must contain exactly "
                         + OrderedHealthGroups.Count + " muscle groups.");
                for (int index = 0; index < OrderedHealthGroups.Count; index++)
                    if (health.GetArrayElementAtIndex(index).intValue
                        != OrderedHealthGroups[index])
                        Fail("PowerLegs health muscle group differs at index " + index + ".");
                ValidateFaceAnim();
                foreach (string positive in new[]
                         {
                             "health.maxHitPoints", "health.maxAppendageHp",
                             "health.stunRecovery", "health.maxStunSeconds",
                         })
                    if (!Finite(Float(_power, positive)) || Float(_power, positive) <= 0f)
                        Fail("PowerLegs " + positive + " must be positive.");
                if (!Near(
                        Float(_power, "health.aggression"),
                        _movement.StartingHostility))
                    Fail("PowerLegs must retain the selected starting hostility.");
                if (!Near(
                        Float(_power, "health.vengefulness"),
                        _movement.RetaliationVengefulness))
                    Fail("PowerLegs must retain the selected damage response.");
                if (!Near(
                        P(config, "healthSettings.aggression").floatValue,
                        _movement.StartingHostility)
                    || !Near(
                        P(config, "healthSettings.vengefulness").floatValue,
                        _movement.RetaliationVengefulness))
                    Fail("The runtime-applied config must preserve the "
                         + "selected hostility response.");

                MarrowNpcToolkitPatch6CompatibilityProbe
                    .ValidateNativePowerAudioState(
                        _definition, _power, _impactSource);
                TrackAsset(MarrowNpcToolkitPatch6CompatibilityProbe
                    .NativeAudioAssets(_definition).ToArray());

                foreach (string field in new[]
                         {
                             "OpenHand", "Fist", "Pistol", "PistolOffhand",
                         })
                {
                    UnityEngine.Object pose = Obj(_power, "handPoser." + field);
                    RequirePersistent(pose, field + " hand pose");
                    TrackAsset(pose);
                }
                int isHuman = Int(_power, "ik.isHuman");
                if (isHuman != 0)
                    Fail("PowerLegs humanoid post-processing must remain disabled.");
                if (P(_power, "handPoser.leftHandRefs").arraySize != 0
                    || P(_power, "handPoser.rightHandRefs").arraySize != 0)
                    Fail("Non-human PowerLegs hand reference arrays must be empty.");

                ValidateAnimatorEvents("onGetUpProne", "GetUpFromFace");
                ValidateAnimatorEvents("onGetUpSupine", "GetUpFromBack");
                TrackAsset(config, standing);
            }

            private void ValidateFaceAnim()
            {
                var serialized = new SerializedObject(_power);
                SerializedProperty enabled = serialized.FindProperty(
                    "faceAnim.faceAnimEnabled");
                if (enabled != null && enabled.intValue != 0)
                    Fail("Patch 6 FaceAnim must remain disabled.");
                SerializedProperty mouth = serialized.FindProperty(
                    "faceAnim.mouthTran");
                UnityEngine.Object expectedMouth = _definition.IncludePhysicalJaw
                    ? _bodies[HumanBodyBones.Jaw].transform
                    : null;
                if (mouth != null && mouth.objectReferenceValue != expectedMouth)
                    Fail("FaceAnim.mouthTran must reference the physical Jaw body.");
                foreach (string field in new[]
                         {
                             "greetings", "agros", "unAgros", "deaths",
                             "painSmalls", "painBigs", "attack1s", "efforts",
                             "eventLines",
                         })
                {
                    SerializedProperty array = serialized.FindProperty(
                        "faceAnim." + field);
                    if (array != null && (!array.isArray || array.arraySize != 0))
                        Fail("FaceAnim." + field + " must remain empty.");
                }
            }

            internal AnimationClip ValidateController(
                RuntimeAnimatorController runtimeController)
            {
                AnimatorOverrideController overrides =
                    runtimeController as AnimatorOverrideController;
                RuntimeAnimatorController baseRuntime = overrides == null
                    ? runtimeController
                    : overrides.runtimeAnimatorController;
                AnimatorController controller = baseRuntime as AnimatorController;
                if (controller == null)
                    Fail("The native Animator controller is not an inspectable "
                         + "AnimatorController/AnimatorOverrideController.");

                var states = new Dictionary<string, List<AnimatorState>>(
                    StringComparer.Ordinal);
                foreach (AnimatorControllerLayer layer in controller.layers)
                    CollectStates(layer.stateMachine, states);
                foreach (string stateName in new[]
                         {
                             "Idle2", "Loco", "GetUpFromFace", "GetUpFromBack",
                         })
                {
                    if (!states.TryGetValue(stateName, out List<AnimatorState> matches)
                        || matches.Count != 1 || matches[0].motion == null)
                        Fail("Animator controller is missing motion-backed state "
                             + stateName + ".");
                    AnimatorState state = matches[0];
                    AnimationClip clip = FirstClip(state.motion, overrides);
                    if (clip == null || clip.length <= 0f)
                        Fail("Animator state " + stateName
                             + " has no usable animation clip.");
                    RequirePersistent(clip, stateName + " animation clip");
                }

                AnimatorControllerLayer[] layers = controller.layers;
                if (layers.Length == 0
                    || !string.Equals(
                        layers[0].name, "Base Layer", StringComparison.Ordinal)
                    || layers[0].stateMachine == null
                    || layers[0].stateMachine.defaultState == null
                    || layers[0].stateMachine.defaultState.motion == null)
                    Fail("Animator Base Layer must have a motion-backed default state.");
                AnimatorState defaultState = layers[0].stateMachine.defaultState;
                AnimationClip defaultClip = FirstClip(
                    defaultState.motion, overrides);
                if (defaultClip == null || defaultClip.length <= 0f)
                    Fail("Animator Base Layer default state has no usable animation clip.");
                RequirePersistent(defaultClip, "Base Layer default animation clip");

                var parameters = controller.parameters.ToDictionary(
                    value => value.name,
                    value => value.type,
                    StringComparer.Ordinal);
                var requiredParameters = new Dictionary<string, AnimatorControllerParameterType>
                {
                    ["syncTime"] = AnimatorControllerParameterType.Float,
                    ["locoCycle"] = AnimatorControllerParameterType.Float,
                    ["m/sec"] = AnimatorControllerParameterType.Float,
                    ["crouch"] = AnimatorControllerParameterType.Bool,
                    ["angry"] = AnimatorControllerParameterType.Bool,
                    ["awake"] = AnimatorControllerParameterType.Bool,
                    ["unGrounded"] = AnimatorControllerParameterType.Bool,
                    ["jump"] = AnimatorControllerParameterType.Trigger,
                    ["preJump"] = AnimatorControllerParameterType.Trigger,
                    ["attack"] = AnimatorControllerParameterType.Trigger,
                    ["idle"] = AnimatorControllerParameterType.Trigger,
                    ["flinch"] = AnimatorControllerParameterType.Trigger,
                };
                foreach (KeyValuePair<string, AnimatorControllerParameterType> pair
                         in requiredParameters)
                    if (!parameters.TryGetValue(pair.Key, out var actual)
                        || actual != pair.Value)
                        Fail("Animator controller parameter " + pair.Key
                             + " is missing or has the wrong type.");

                AnimationClip idle = FirstClip(states["Idle2"][0].motion, overrides);
                TrackAsset(controller, idle);
                return idle;
            }

            private static void CollectStates(
                AnimatorStateMachine machine,
                IDictionary<string, List<AnimatorState>> result)
            {
                if (machine == null) return;
                foreach (ChildAnimatorState child in machine.states)
                {
                    if (child.state == null) continue;
                    if (!result.TryGetValue(child.state.name, out var matches))
                    {
                        matches = new List<AnimatorState>();
                        result.Add(child.state.name, matches);
                    }
                    matches.Add(child.state);
                }
                foreach (ChildAnimatorStateMachine child in machine.stateMachines)
                    CollectStates(child.stateMachine, result);
            }

            private static AnimationClip FirstClip(
                Motion motion,
                AnimatorOverrideController overrides)
            {
                if (motion is AnimationClip clip)
                {
                    AnimationClip replacement = overrides == null ? null : overrides[clip];
                    return replacement != null ? replacement : clip;
                }
                if (motion is BlendTree tree)
                    foreach (ChildMotion child in tree.children)
                    {
                        AnimationClip found = FirstClip(child.motion, overrides);
                        if (found != null) return found;
                    }
                return null;
            }

            private void ValidateStandingPose(
                UnityEngine.Object pose,
                SerializedProperty positions,
                SerializedProperty rotations)
            {
                for (int index = 0; index < positions.arraySize; index++)
                {
                    Vector3 position = positions.GetArrayElementAtIndex(index)
                        .vector3Value;
                    Quaternion rotation = rotations.GetArrayElementAtIndex(index)
                        .quaternionValue;
                    if (!Finite(position) || !Normalized(rotation))
                        Fail("Standing pose contains invalid data at muscle " + index + ".");
                }
                // EnemyPoseData is a configured runtime target pose, not an
                // animation-frame cache. Different valid PowerLegs templates
                // may use a neutral/startup pose that intentionally differs
                // from Idle2. Validate the exact persistent 16-muscle data
                // here; controller states and motions are audited separately.
                TrackAsset(pose);
            }

            private Transform ResolveAcceptedBone(
                Transform avatarRoot,
                HumanBodyBones role)
            {
                if (role == HumanBodyBones.Jaw)
                {
                    string jawPath = _definition.AvatarSourceProfile.JawPath;
                    Transform jaw = string.IsNullOrWhiteSpace(jawPath)
                        ? null
                        : avatarRoot.Find(jawPath);
                    if (jaw == null)
                        Fail("Accepted source profile does not resolve its durable Jaw path.");
                    return jaw;
                }
                NpcHumanoidBoneBinding[] bindings = _definition.AvatarSourceProfile
                    .HumanoidBones.Where(value => value.Role == role).ToArray();
                if (bindings.Length != 1)
                    Fail("Accepted source profile does not uniquely bind " + role + ".");
                string path = bindings[0].TransformPath;
                Transform result = string.IsNullOrWhiteSpace(path)
                    ? avatarRoot
                    : avatarRoot.Find(path);
                if (result == null)
                    Fail("Accepted source path for " + role + " is missing.");
                return result;
            }

            private void ValidateLimbIk()
            {
                Component[] all = _animationRoot.GetComponentsInChildren(
                        _limbIkType, true)
                    .Cast<Component>().ToArray();
                if (all.Length != 4)
                    Fail("AnimationRoot must contain exactly four LimbIKSlz solvers.");

                Transform avatarRoot = _animationRoot.childCount == 1
                    ? _animationRoot.GetChild(0)
                    : null;
                if (avatarRoot == null)
                    Fail("Limb IK validation has no routed Avatar root.");
                var expected = new[]
                {
                    new LimbExpectation(
                        HumanBodyBones.LeftUpperLeg,
                        HumanBodyBones.LeftLowerLeg,
                        HumanBodyBones.LeftFoot,
                        "NeutralRoot/Foot_L/Ankle_L_IKtarget"),
                    new LimbExpectation(
                        HumanBodyBones.RightUpperLeg,
                        HumanBodyBones.RightLowerLeg,
                        HumanBodyBones.RightFoot,
                        "NeutralRoot/Foot_R/Ankle_R_IKtarget"),
                    new LimbExpectation(
                        HumanBodyBones.LeftUpperArm,
                        HumanBodyBones.LeftLowerArm,
                        HumanBodyBones.LeftHand,
                        "Hand_L/hand_L_target"),
                    new LimbExpectation(
                        HumanBodyBones.RightUpperArm,
                        HumanBodyBones.RightLowerArm,
                        HumanBodyBones.RightHand,
                        "Hand_R/hand_R_target"),
                };
                var byUpperRole = new Dictionary<HumanBodyBones, Component>();
                foreach (LimbExpectation value in expected)
                {
                    Transform upper = ResolveAcceptedBone(avatarRoot, value.Upper);
                    Transform lower = ResolveAcceptedBone(avatarRoot, value.Lower);
                    Transform end = ResolveAcceptedBone(avatarRoot, value.End);
                    Transform target = _aiRig.Find(value.TargetPath);
                    Component solver = RequireOnly(
                        upper.GetComponents(_limbIkType).Cast<Component>().ToArray(),
                        value.Upper + " LimbIKSlz");
                    if (solver is Behaviour behaviour && behaviour.enabled)
                        Fail(value.Upper + " LimbIKSlz must remain disabled at authoring.");
                    if (Obj(solver, "animator") != null
                        || Obj(solver, "solver.root") != upper
                        || Obj(solver, "solver.target") != target)
                        Fail(value.Upper + " LimbIKSlz root/target references are invalid.");
                    Transform[] chain = { upper, lower, end };
                    for (int index = 0; index < chain.Length; index++)
                    {
                        string prefix = "solver.bone" + (index + 1) + ".";
                        if (Obj(solver, prefix + "transform") != chain[index]
                            || !Near(P(solver, prefix + "defaultLocalPosition")
                                .vector3Value, chain[index].localPosition)
                            || !Near(P(solver, prefix + "defaultLocalRotation")
                                .quaternionValue, chain[index].localRotation)
                            || !Near(P(solver, prefix + "solverPosition")
                                .vector3Value, chain[index].position, 0.0005f)
                            || !Near(P(solver, prefix + "solverRotation")
                                .quaternionValue, chain[index].rotation, 0.0005f))
                            Fail(value.Upper + " LimbIKSlz bone " + index
                                 + " caches are stale.");
                        if (index < 2)
                        {
                            Vector3 delta = chain[index + 1].position
                                            - chain[index].position;
                            float sqrMagnitude = P(solver, prefix + "sqrMag")
                                .floatValue;
                            Vector3 axis = P(solver, prefix + "axis").vector3Value;
                            if (!Near(sqrMagnitude, delta.sqrMagnitude, 0.0005f)
                                || !Near(axis, chain[index]
                                    .InverseTransformDirection(delta).normalized,
                                    0.0005f))
                                Fail(value.Upper + " LimbIKSlz segment caches are stale.");
                        }
                    }
                    if (target == null
                        || !Near(P(solver, "solver.IKPosition").vector3Value,
                            target.position, 0.0005f)
                        || !Near(P(solver, "solver.IKRotation").quaternionValue,
                            target.rotation, 0.0005f)
                        || !Finite(P(solver, "solver.bendNormal").vector3Value))
                        Fail(value.Upper + " LimbIKSlz target caches are invalid.");
                    byUpperRole.Add(value.Upper, solver);
                    Track(solver);
                }

                ValidateReferences(P(_power, "ik.footIkSolvers"),
                    byUpperRole[HumanBodyBones.LeftUpperLeg],
                    byUpperRole[HumanBodyBones.RightUpperLeg]);
                ValidateReferences(P(_power, "ik.armIkSolvers"),
                    byUpperRole[HumanBodyBones.LeftUpperArm],
                    byUpperRole[HumanBodyBones.RightUpperArm]);
                Transform leftToe = ResolveAcceptedBone(avatarRoot, HumanBodyBones.LeftToes);
                Transform rightToe = ResolveAcceptedBone(avatarRoot, HumanBodyBones.RightToes);
                ValidateReferences(P(_power, "ik.toeTrans"), leftToe, rightToe);
                if (Obj(_power, "ik.lfHandTarget") != _aiRig.Find("Hand_L")
                    || Obj(_power, "ik.rtHandTarget") != _aiRig.Find("Hand_R")
                    || Obj(_power, "ik.lfHandAnim")
                        != ResolveAcceptedBone(avatarRoot, HumanBodyBones.LeftHand)
                    || Obj(_power, "ik.rtHandAnim")
                        != ResolveAcceptedBone(avatarRoot, HumanBodyBones.RightHand)
                    || Int(_power, "ik.lfShoulderMuscleIndex") != 10
                    || Int(_power, "ik.rtShoulderMuscleIndex") != 13)
                    Fail("PowerLegs IK arrays/hand routes do not match the four solvers.");
            }

            private void ValidateSkinnedBoneRebind()
            {
                SkinnedMeshRenderer[] renderers = _animationRoot
                    .GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Component[] allRebinds = _root.GetComponentsInChildren(
                        _rebindType, true)
                    .Cast<Component>().ToArray();
                if (renderers.Length == 0 || allRebinds.Length != renderers.Length)
                    Fail("Every SkinnedMeshRenderer must have exactly one bone rebind.");
                Transform sourceJaw = null;
                Transform physicalJaw = null;
                bool sawSourceJaw = false;
                if (_definition.IncludePhysicalJaw)
                {
                    if (_animationRoot.childCount != 1)
                        Fail("AnimationRoot must retain one routed Avatar root.");
                    sourceJaw = ResolveAcceptedBone(
                        _animationRoot.GetChild(0), HumanBodyBones.Jaw);
                    physicalJaw = _bodies[HumanBodyBones.Jaw].transform;
                }
                foreach (SkinnedMeshRenderer renderer in renderers)
                {
                    Component rebind = RequireOnly(
                        renderer.GetComponents(_rebindType).Cast<Component>().ToArray(),
                        renderer.name + " SkinnedBoneRebind");
                    SerializedProperty bones = Arr(
                        rebind, "bones", renderer.bones.Length);
                    SerializedProperty mask = Arr(
                        rebind, "rebindBone", renderer.bones.Length);
                    if (Obj(rebind, "skinnedMeshRenderer") != renderer
                        || Obj(rebind, "meshToRead") != null
                        || Obj(rebind, "meshToWrite") != null
                        || !renderer.updateWhenOffscreen)
                        Fail(renderer.name + " has an incomplete bone-rebind contract.");
                    for (int index = 0; index < bones.arraySize; index++)
                    {
                        Transform source = renderer.bones[index];
                        Transform target = bones.GetArrayElementAtIndex(index)
                            .objectReferenceValue as Transform;
                        if (target == null || target.IsChildOf(_animationRoot)
                            || !(target == _root.transform
                                 || target.IsChildOf(_root.transform))
                            || mask.GetArrayElementAtIndex(index).intValue != 0)
                            Fail(renderer.name + " rebind target differs at bone "
                                 + index + ".");
                        if (_definition.IncludePhysicalJaw && source == sourceJaw)
                        {
                            sawSourceJaw = true;
                            if (target != physicalJaw
                                || target.name.StartsWith(
                                    "__MARROW_NPC_BRIDGE__",
                                    StringComparison.Ordinal))
                                Fail(renderer.name + " source Jaw slot must map "
                                     + "directly to the physical Jaw body.");
                        }
                    }
                    Track(rebind, renderer);
                }
                if (_definition.IncludePhysicalJaw && !sawSourceJaw)
                    Fail("No skinned renderer slot uses the accepted Avatar Jaw source.");
            }

            private readonly struct LimbExpectation
            {
                public HumanBodyBones Upper { get; }
                public HumanBodyBones Lower { get; }
                public HumanBodyBones End { get; }
                public string TargetPath { get; }

                public LimbExpectation(
                    HumanBodyBones upper,
                    HumanBodyBones lower,
                    HumanBodyBones end,
                    string targetPath)
                {
                    Upper = upper;
                    Lower = lower;
                    End = end;
                    TargetPath = targetPath;
                }
            }

            private void ValidateAnimatorEvents(string path, string requiredState)
            {
                SerializedProperty events = P(_power, path);
                if (!events.isArray || events.arraySize == 0)
                    Fail("PowerLegs." + path + " must contain a recovery state.");
                bool found = false;
                for (int index = 0; index < events.arraySize; index++)
                {
                    string state = Rel(
                        events.GetArrayElementAtIndex(index), "animationState")
                        .stringValue;
                    found |= string.Equals(state, requiredState, StringComparison.Ordinal);
                }
                if (!found)
                    Fail("PowerLegs." + path + " does not contain " + requiredState + ".");
            }

            private void ValidateAgentLink()
            {
                if (_agentLink is Behaviour behaviour && behaviour.enabled)
                    Fail("AgentLinkControl must remain disabled in the baseline prefab.");
                if (Obj(_agentLink, "navAgent") != _navAgent
                    || Obj(_agentLink, "brain") != _brain
                    || Obj(_agentLink, "triggerProxy") != _proxy
                    || Obj(_agentLink, "baseBehaviour") != _power
                    || Obj(_agentLink, "legBehaviour") != _power
                    || Obj(_agentLink, "_puppet") != _puppet
                    || !Near(Float(_agentLink, "totalMass"), 0f)
                    || !Near(Float(_agentLink, "jointForceMult"), 1f)
                    || !Near(Float(_agentLink, "minLinkDupeDuration"), 2f)
                    || !Near(Float(_agentLink, "distTimer"), 5f))
                    Fail("AgentLinkControl references/defaults differ from accepted Patch 6.");

                var roleFields = new Dictionary<string, HumanBodyBones>
                {
                    ["headRB"] = HumanBodyBones.Head,
                    ["chestRB"] = HumanBodyBones.Chest,
                    ["leftHandRB"] = HumanBodyBones.LeftHand,
                    ["leftElbowRB"] = HumanBodyBones.LeftLowerArm,
                    ["rightHandRB"] = HumanBodyBones.RightHand,
                    ["rightElbowRB"] = HumanBodyBones.RightLowerArm,
                    ["leftFootRB"] = HumanBodyBones.LeftFoot,
                    ["leftKneeRB"] = HumanBodyBones.LeftLowerLeg,
                    ["rightFootRB"] = HumanBodyBones.RightFoot,
                    ["rightKneeRB"] = HumanBodyBones.RightLowerLeg,
                };
                foreach (KeyValuePair<string, HumanBodyBones> pair in roleFields)
                    if (Obj(_agentLink, pair.Key) != _bodies[pair.Value])
                        Fail("AgentLinkControl." + pair.Key + " is miswired.");
                SerializedProperty allBodies = Arr(
                    _agentLink, "allRBs", OrderedRoles.Count);
                for (int index = 0; index < OrderedRoles.Count; index++)
                    if (allBodies.GetArrayElementAtIndex(index).objectReferenceValue
                        != _bodies[OrderedRoles[index]])
                        Fail("AgentLinkControl allRBs differs at "
                             + OrderedRoles[index] + ".");
                foreach (string field in AgentLinkNullFields)
                    if (Obj(_agentLink, field) != null)
                        Fail("AgentLinkControl." + field + " must be null at authoring time.");
            }

            private void ValidateLocomotionAndNavigation()
            {
                if (_liteLoco.transform != _aiRig || _navAgent.transform != _aiRig
                    || Obj(_liteLoco, "root") != _aiRig)
                    Fail("LiteLoco and NavMeshAgent must live on direct AiRig.");
                Transform neutralRoot = Obj(_liteLoco, "neutralRoot") as Transform;
                if (neutralRoot == null || neutralRoot.parent != _aiRig
                    || !string.Equals(neutralRoot.name, "NeutralRoot",
                        StringComparison.Ordinal))
                    Fail("LiteLoco.neutralRoot must be direct AiRig/NeutralRoot.");
                if (!Near(Float(_liteLoco, "weight"), 1f))
                    Fail("LiteLoco weight must be 1.");

                SerializedProperty groups = Arr(_liteLoco, "stepGroups", 1);
                SerializedProperty group = groups.GetArrayElementAtIndex(0);
                Transform pelvis = Rel(group, "pelvis").objectReferenceValue as Transform;
                if (pelvis == null || !pelvis.IsChildOf(_aiRig)
                    || Rel(group, "sisterStepGroup").intValue != -1
                    || Rel(group, "legLength").floatValue <= 0f)
                    Fail("LiteLoco step group has no valid pelvis/leg contract.");
                RequireCurve(Rel(group, "FootXVCurve"), "FootXVCurve");
                SerializedProperty gears = Rel(group, "gears");
                if (!gears.isArray || gears.arraySize == 0)
                    Fail("LiteLoco requires at least one complete gait gear.");
                for (int index = 0; index < gears.arraySize; index++)
                {
                    SerializedProperty gear = gears.GetArrayElementAtIndex(index);
                    foreach (string curve in new[]
                             {
                                 "StepRateVCurve", "stepHeight", "StepZInterp",
                                 "StepAnkleBend", "MuscleUsage",
                             })
                        RequireCurve(Rel(gear, curve), "gear " + index + " " + curve);
                }
                SerializedProperty grounder = Rel(group, "grounder");
                if (Rel(Rel(grounder, "layers"), "m_Bits").intValue != 123843841
                    || !Finite(Rel(grounder, "maxStep").floatValue)
                    || Rel(grounder, "maxStep").floatValue <= 0f
                    || !Finite(Rel(grounder, "footSpeed").floatValue)
                    || Rel(grounder, "footSpeed").floatValue <= 0f)
                    Fail("LiteLoco grounder has invalid layers or movement values.");

                SerializedProperty footsteps = Rel(group, "footsteps");
                if (!footsteps.isArray || footsteps.arraySize != 2)
                    Fail("LiteLoco must contain exactly left/right footsteps.");
                Component sharedStepSfx = null;
                UnityEngine.Object sharedLifted = null;
                for (int index = 0; index < 2; index++)
                {
                    HumanBodyBones role = index == 0
                        ? HumanBodyBones.LeftFoot
                        : HumanBodyBones.RightFoot;
                    SerializedProperty step = footsteps.GetArrayElementAtIndex(index);
                    Transform hip = Rel(step, "hip").objectReferenceValue as Transform;
                    Transform foot = Rel(step, "foot").objectReferenceValue as Transform;
                    Transform neutral = Rel(step, "neutralTarget")
                        .objectReferenceValue as Transform;
                    Collider footCollider = Rel(step, "footCollider")
                        .objectReferenceValue as Collider;
                    Component stepSfx = Rel(step, "stepSfx")
                        .objectReferenceValue as Component;
                    UnityEngine.Object lifted = Rel(step, "liftedMat").objectReferenceValue;
                    if (hip == null || foot == null || neutral == null
                        || !hip.IsChildOf(_aiRig) || !foot.IsChildOf(_aiRig)
                        || !neutral.IsChildOf(_aiRig)
                        || footCollider != _primaryColliders[role]
                        || stepSfx == null || !stepSfx.transform.IsChildOf(_aiRig))
                        Fail(role + " LiteLoco footstep has invalid helper/body references.");
                    RequirePersistent(lifted, role + " lifted foot material");
                    RequirePersistent(footCollider.sharedMaterial,
                        role + " planted foot material");
                    ValidateFootMaterials(footCollider.sharedMaterial,
                        lifted as PhysicMaterial, role);
                    if (index == 0)
                    {
                        sharedStepSfx = stepSfx;
                        sharedLifted = lifted;
                    }
                    else if (stepSfx != sharedStepSfx || lifted != sharedLifted)
                        Fail("Left/right footsteps must share FootstepSFX and lifted material.");
                    Track(stepSfx);
                    TrackAsset(lifted, footCollider.sharedMaterial);
                }
                ValidateFootstepSfx(sharedStepSfx);

                var nav = new SerializedObject(_navAgent);
                foreach (string positive in new[]
                         {
                             "m_Radius", "m_Height", "m_Speed", "m_Acceleration",
                             "m_AngularSpeed",
                         })
                    if (!Finite(Prop(nav, positive).floatValue)
                        || Prop(nav, positive).floatValue <= 0f)
                        Fail("NavMeshAgent." + positive + " must be positive.");
                if (!Finite(Prop(nav, "m_BaseOffset").floatValue)
                    || !Finite(Prop(nav, "m_StoppingDistance").floatValue)
                    || Prop(nav, "m_StoppingDistance").floatValue < 0f)
                    Fail("NavMeshAgent base offset/stopping distance is invalid.");
                if (_movement != null && !_movement.UsesLegacyFallback)
                {
                    RequireNear(nav, "m_Radius", _movement.NavRadius);
                    RequireNear(nav, "m_Height", _movement.NavHeight);
                    RequireNear(
                        nav,
                        "m_BaseOffset",
                        ExpectedMovementNavBaseOffset());
                    RequireNear(nav, "m_Speed", _movement.WalkSpeed);
                    RequireNear(nav, "m_Acceleration", _movement.Acceleration);
                    RequireNear(nav, "m_AngularSpeed", _movement.AngularSpeed);
                    RequireNear(
                        nav, "m_StoppingDistance", _movement.StoppingDistance);
                    RequireNear(_power, "roamSpeed", _movement.PowerRoamSpeed);
                    RequireNear(_power, "agroedSpeed", _movement.PowerAgroSpeed);
                    RequireNear(
                        _power, "engagedSpeed", _movement.PowerEngagedSpeed);
                    RequireNear(
                        _power, "roamAngSpeed", _movement.PowerAngularSpeed);
                    RequireNear(
                        _power, "agroedAngSpeed", _movement.PowerAngularSpeed);
                }
                if (_navAgent is Behaviour navBehaviour && !navBehaviour.enabled)
                    Fail("NavMeshAgent must be enabled.");
                if (Prop(nav, "m_WalkableMask").intValue == 0
                    || Prop(nav, "m_AutoTraverseOffMeshLink").boolValue
                    || !Prop(nav, "m_AutoBraking").boolValue
                    || !Prop(nav, "m_AutoRepath").boolValue)
                    Fail("NavMeshAgent traversal/repath defaults differ from the baseline.");
            }

            private float ExpectedMovementNavBaseOffset()
            {
                if (_movement == null || _movement.UsesLegacyFallback)
                    return Prop(
                        new SerializedObject(_navAgent), "m_BaseOffset").floatValue;
                if (_animationRoot == null || _animator == null
                    || !_animator.transform.IsChildOf(_animationRoot))
                    Fail("Movement validation could not resolve the routed Avatar root.");
                Transform movementFrame = _animator.transform;
                while (movementFrame.parent != _animationRoot)
                {
                    movementFrame = movementFrame.parent;
                    if (movementFrame == null)
                        Fail("The routed Animator has no Avatar root beneath AnimationRoot.");
                }
                Vector3 solePlane = movementFrame.position
                                    + movementFrame.up.normalized
                                    * _movement.SoleHeight;
                return Vector3.Dot(
                    solePlane - _root.transform.position,
                    _root.transform.up.normalized);
            }

            private void ValidateFootstepSfx(Component component)
            {
                if (component == null)
                    Fail("LiteLoco has no FootstepSFX component.");
                MarrowNpcToolkitPatch6CompatibilityProbe
                    .ValidateNativeFootstepAudioState(_definition, component);
            }

            private void ValidateFootMaterials(
                PhysicMaterial planted,
                PhysicMaterial lifted,
                HumanBodyBones role)
            {
                if (planted == null || lifted == null)
                    Fail(role + " is missing planted/lifted PhysicMaterials.");
                if (!Near(planted.dynamicFriction, 0.9f)
                    || !Near(planted.staticFriction, 1.1f)
                    || !Near(planted.bounciness, 0f)
                    || planted.frictionCombine != PhysicMaterialCombine.Multiply
                    || planted.bounceCombine != PhysicMaterialCombine.Minimum
                    || !Near(lifted.dynamicFriction, 0.1f)
                    || !Near(lifted.staticFriction, 0.2f)
                    || !Near(lifted.bounciness, 0.15f)
                    || lifted.frictionCombine != PhysicMaterialCombine.Multiply
                    || lifted.bounceCombine != PhysicMaterialCombine.Multiply)
                    Fail(role + " planted/lifted PhysicMaterials differ from baseline.");
            }

            private void ValidateVisualDamage()
            {
                Renderer[] expected = _animationRoot.GetComponentsInChildren<Renderer>(true);
                SerializedProperty renderers = P(_visualDamage, "Renderers");
                if (expected.Length == 0 || !renderers.isArray
                    || renderers.arraySize != expected.Length)
                    Fail("VisualDamageController does not register all Avatar renderers.");
                for (int index = 0; index < expected.Length; index++)
                    if (renderers.GetArrayElementAtIndex(index).objectReferenceValue
                        != expected[index])
                        Fail("VisualDamageController renderer order differs at " + index + ".");
                if (!Near(Float(_visualDamage, "meshScaleFactor"), 1f)
                    || !Near(Float(_visualDamage, "hitScaleFactor"), 1f))
                    Fail("VisualDamageController scale factors must be 1.");

                if (_root.GetComponentsInChildren(_impactPropertiesType, true).Length
                        != OrderedRoles.Count
                    || _root.GetComponentsInChildren(_damageReceiverType, true).Length
                        != OrderedRoles.Count)
                    Fail("The native NPC must contain exactly one ImpactProperties "
                         + "and VisualDamageReceiver per physical body.");

                foreach (HumanBodyBones role in OrderedRoles)
                {
                    Transform body = _bodies[role].transform;
                    Component impact = RequireOnly(
                        body.GetComponents(_impactPropertiesType)
                            .Cast<Component>().ToArray(),
                        role + " ImpactProperties");
                    Component receiver = RequireOnly(
                        body.GetComponents(_damageReceiverType)
                            .Cast<Component>().ToArray(),
                        role + " VisualDamageReceiver");
                    if (!string.Equals(
                            P(impact, "_surfaceDataCard._barcode._id").stringValue,
                            BloodSurface,
                            StringComparison.Ordinal)
                        || P(impact, "decalType").intValue
                            != BloodColliderDecalType)
                        Fail(role + " does not use the native blood-impact surface.");
                    if (Obj(receiver, "bone") != null
                        || Obj(receiver, "visualDamageController") != _visualDamage)
                        Fail(role + " VisualDamageReceiver is not wired to the "
                             + "root VisualDamageController.");
                    Track(impact, receiver);
                }
            }

            private void ValidateConfiguredProvenance()
            {
                UnityEngine.Object config = Obj(_power, "prefabConfig");
                UnityEngine.Object standing = Obj(_power, "standingIdle");
                UnityEngine.Object openHand = Obj(_power, "handPoser.OpenHand");
                if (config == null || standing == null || openHand == null)
                    Fail("PowerLegs must reference a persistent movement config, "
                         + "standing pose, and open-hand pose before their "
                         + "project provenance can be validated.");
                Type configType = config.GetType();
                Type poseType = standing.GetType();
                Type handType = openHand.GetType();
                if (!MarrowNpcToolkitPatch6BehaviourSettings.TryResolve(
                        configType,
                        poseType,
                        handType,
                        _definition.IncludePhysicalJaw,
                        out var settings,
                        out string detail))
                    Fail("Patch 6 Behaviour Settings are incomplete: " + detail);
                _movement = Patch6MovementBuildSettings.Resolve(
                    _definition,
                    settings,
                    configType,
                    poseType,
                    _definition.IncludePhysicalJaw,
                    false);
                if (Obj(_power, "prefabConfig") != _movement.MovementConfig
                    || Obj(_power, "overrideConfig") != _movement.MovementConfig
                    || Obj(_power, "standingIdle") != _movement.StandingPose
                    || Obj(_power, "handPoser.OpenHand") != settings.OpenHand
                    || Obj(_power, "handPoser.Fist") != settings.Fist
                    || Obj(_power, "handPoser.Pistol") != settings.Pistol
                    || Obj(_power, "handPoser.PistolOffhand") != settings.PistolOffhand)
                    Fail("Saved PowerLegs assets do not match explicit project settings.");
                RequirePersistent(settings.BehaviourTemplate, "Behaviour Template");
                TrackAsset(settings.BehaviourTemplate, settings.BaseEnemyConfig,
                    settings.StandingIdle, _movement.MovementConfig,
                    _movement.StandingPose, _movement.Profile,
                    settings.OpenHand, settings.Fist,
                    settings.Pistol, settings.PistolOffhand);
            }

            private void AppendSerializedObject(StringBuilder text, Component component)
            {
                text.Append("component=").Append(ObjectKey(component)).Append('|');
                var serialized = new SerializedObject(component);
                serialized.UpdateIfRequiredOrScript();
                SerializedProperty iterator = serialized.GetIterator();
                bool enter = true;
                while (iterator.Next(enter))
                {
                    if (IsPrefabSerializationBookkeeping(iterator.propertyPath))
                    {
                        enter = false;
                        continue;
                    }
                    enter = true;
                    text.Append(iterator.propertyPath).Append('=');
                    AppendValue(text, iterator);
                    text.Append('|');
                    if (iterator.propertyType
                            == SerializedPropertyType.ObjectReference
                        || iterator.propertyType
                            == SerializedPropertyType.ExposedReference)
                        enter = false;
                }
            }

            private static bool IsPrefabSerializationBookkeeping(string path)
            {
                return string.Equals(
                           path,
                           "m_CorrespondingSourceObject",
                           StringComparison.Ordinal)
                       || path.StartsWith(
                           "m_CorrespondingSourceObject.",
                           StringComparison.Ordinal)
                       || string.Equals(
                           path,
                           "m_PrefabInstance",
                           StringComparison.Ordinal)
                       || path.StartsWith(
                           "m_PrefabInstance.",
                           StringComparison.Ordinal)
                       || string.Equals(
                           path,
                           "m_PrefabAsset",
                           StringComparison.Ordinal)
                       || path.StartsWith(
                           "m_PrefabAsset.",
                           StringComparison.Ordinal);
            }

            private void AppendValue(StringBuilder text, SerializedProperty property)
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                    case SerializedPropertyType.Character:
                    case SerializedPropertyType.ArraySize:
                        text.Append(property.intValue);
                        return;
                    case SerializedPropertyType.Boolean:
                        text.Append(property.boolValue ? '1' : '0');
                        return;
                    case SerializedPropertyType.Float:
                        text.Append(F(property.floatValue));
                        return;
                    case SerializedPropertyType.String:
                        text.Append(property.stringValue ?? string.Empty);
                        return;
                    case SerializedPropertyType.Color:
                        Color color = property.colorValue;
                        text.Append(F(color.r)).Append(',').Append(F(color.g)).Append(',')
                            .Append(F(color.b)).Append(',').Append(F(color.a));
                        return;
                    case SerializedPropertyType.ObjectReference:
                    case SerializedPropertyType.ExposedReference:
                        text.Append(ObjectKey(property.objectReferenceValue));
                        return;
                    case SerializedPropertyType.Enum:
                        text.Append(property.enumValueIndex);
                        return;
                    case SerializedPropertyType.Vector2:
                        Vector2 v2 = property.vector2Value;
                        text.Append(F(v2.x)).Append(',').Append(F(v2.y));
                        return;
                    case SerializedPropertyType.Vector3:
                        Vector3 v3 = property.vector3Value;
                        AppendVector(text, v3);
                        return;
                    case SerializedPropertyType.Vector4:
                        Vector4 v4 = property.vector4Value;
                        text.Append(F(v4.x)).Append(',').Append(F(v4.y)).Append(',')
                            .Append(F(v4.z)).Append(',').Append(F(v4.w));
                        return;
                    case SerializedPropertyType.Quaternion:
                        Quaternion q = property.quaternionValue;
                        text.Append(F(q.x)).Append(',').Append(F(q.y)).Append(',')
                            .Append(F(q.z)).Append(',').Append(F(q.w));
                        return;
                    case SerializedPropertyType.Rect:
                        Rect rect = property.rectValue;
                        text.Append(F(rect.x)).Append(',').Append(F(rect.y)).Append(',')
                            .Append(F(rect.width)).Append(',').Append(F(rect.height));
                        return;
                    case SerializedPropertyType.Bounds:
                        Bounds bounds = property.boundsValue;
                        AppendVector(text, bounds.center);
                        text.Append(',');
                        AppendVector(text, bounds.size);
                        return;
                    case SerializedPropertyType.AnimationCurve:
                        AnimationCurve curve = property.animationCurveValue;
                        text.Append((int)curve.preWrapMode).Append(',')
                            .Append((int)curve.postWrapMode);
                        foreach (Keyframe key in curve.keys)
                            text.Append(';').Append(F(key.time)).Append(',')
                                .Append(F(key.value)).Append(',').Append(F(key.inTangent))
                                .Append(',').Append(F(key.outTangent)).Append(',')
                                .Append(F(key.inWeight)).Append(',').Append(F(key.outWeight))
                                .Append(',').Append((int)key.weightedMode);
                        return;
                    default:
                        text.Append(property.propertyType).Append(':')
                            .Append(property.type ?? string.Empty);
                        return;
                }
            }

            private string ObjectKey(UnityEngine.Object value)
            {
                if (value == null) return "null";
                if (value is GameObject gameObject
                    && (gameObject.transform == _root.transform
                        || gameObject.transform.IsChildOf(_root.transform)))
                    return "go:" + Path(gameObject.transform);
                if (value is Component component
                    && (component.transform == _root.transform
                        || component.transform.IsChildOf(_root.transform)))
                {
                    Component[] peers = component.gameObject.GetComponents(
                        component.GetType());
                    int ordinal = Array.IndexOf(peers, component);
                    return "component:" + Path(component.transform) + ":"
                           + component.GetType().FullName + ":" + ordinal;
                }
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        value, out string guid, out long localId)
                    && !string.IsNullOrWhiteSpace(guid))
                {
                    string path = AssetDatabase.GetAssetPath(value);
                    Hash128 dependencyHash = string.IsNullOrWhiteSpace(path)
                        ? default
                        : AssetDatabase.GetAssetDependencyHash(path);
                    return "asset:" + guid + ":" + localId + ":"
                           + value.GetType().FullName + ":dependency="
                           + dependencyHash;
                }
                Fail("Non-local, non-persistent object reference: " + value.name
                     + " (" + value.GetType().FullName + ").");
                return string.Empty;
            }

            private string Path(Transform value)
            {
                if (value == _root.transform) return ".";
                var names = new Stack<string>();
                Transform current = value;
                while (current != null && current != _root.transform)
                {
                    names.Push(current.name);
                    current = current.parent;
                }
                if (current != _root.transform)
                    Fail("Object is outside the native output root: " + value.name + ".");
                return string.Join("/", names);
            }

            private void Track(params Component[] components)
            {
                _fingerprintedComponents.AddRange(components.Where(value => value != null));
            }

            private void TrackAsset(params UnityEngine.Object[] assets)
            {
                _fingerprintedAssets.AddRange(assets.Where(value => value != null));
            }

            private static readonly string[] AgentLinkNullFields =
            {
                "frozenCrabJumpTargetObj", "zipStick", "zipGripBody", "owner",
                "headJoint", "chestJoint", "leftElbowJoint", "rightElbowJoint",
                "leftHandJoint", "rightHandJoint", "leftKneeJoint", "rightKneeJoint",
                "leftFootJoint", "rightFootJoint", "playerProxy",
            };

            private static Type Resolve(string name, string assembly)
            {
                Type value = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(item => string.Equals(
                        item.GetName().Name, assembly, StringComparison.Ordinal))
                    .Select(item => item.GetType(name, false))
                    .FirstOrDefault(item => item != null);
                if (value == null || !typeof(Component).IsAssignableFrom(value))
                    Fail(name + " is unavailable from " + assembly + ".");
                return value;
            }

            private Component RequireSingletonAt(
                Transform owner, Type type, string label)
            {
                Component[] values = owner.GetComponents(type).Cast<Component>().ToArray();
                return RequireOnly(values, label + " on " + Path(owner));
            }

            private static T RequireOnly<T>(T[] values, string label)
            {
                if (values == null || values.Length != 1 || values[0] == null)
                    Fail("Expected exactly one " + label + "; found "
                         + (values?.Length ?? 0) + ".");
                return values[0];
            }

            private static Transform DirectChild(Transform parent, string name)
            {
                if (parent == null) Fail("Cannot resolve child " + name + " from null.");
                Transform[] matches = Enumerable.Range(0, parent.childCount)
                    .Select(parent.GetChild)
                    .Where(value => string.Equals(value.name, name,
                        StringComparison.Ordinal))
                    .ToArray();
                return RequireOnly(matches, "direct child " + name);
            }

            private static Transform UniqueNamedDescendant(Transform root, string name)
            {
                Transform[] matches = root.GetComponentsInChildren<Transform>(true)
                    .Where(value => value != root && string.Equals(
                        value.name, name, StringComparison.Ordinal))
                    .ToArray();
                return RequireOnly(matches, "Physics transform " + name);
            }

            private static Rigidbody OwningRigidbody(Transform value)
            {
                Transform current = value;
                while (current != null)
                {
                    Rigidbody rigidbody = current.GetComponent<Rigidbody>();
                    if (rigidbody != null) return rigidbody;
                    current = current.parent;
                }
                return null;
            }

            private static SerializedProperty P(Component owner, string path)
            {
                return Prop(new SerializedObject(owner), path);
            }

            private static SerializedProperty P(UnityEngine.Object owner, string path)
            {
                return Prop(new SerializedObject(owner), path);
            }

            private static SerializedProperty Prop(
                SerializedObject owner, string path)
            {
                SerializedProperty property = owner?.FindProperty(path);
                if (property == null)
                    Fail((owner?.targetObject?.GetType().FullName ?? "<null>")
                         + " has no serialized property " + path + ".");
                return property;
            }

            private static SerializedProperty Rel(
                SerializedProperty owner, string name)
            {
                SerializedProperty property = owner?.FindPropertyRelative(name);
                if (property == null)
                    Fail((owner?.propertyPath ?? "<null>")
                         + " has no relative property " + name + ".");
                return property;
            }

            private static UnityEngine.Object Obj(Component owner, string path)
            {
                return P(owner, path).objectReferenceValue;
            }

            private static int Int(Component owner, string path)
            {
                return P(owner, path).intValue;
            }

            private static float Float(Component owner, string path)
            {
                return P(owner, path).floatValue;
            }

            private static void RequireNear(
                SerializedObject owner,
                string path,
                float expected)
            {
                float actual = Prop(owner, path).floatValue;
                if (!Near(actual, expected))
                    Fail(owner.targetObject.GetType().FullName + "." + path
                         + " differs from Movement Profile value "
                         + F(expected) + ".");
            }

            private static void RequireNear(
                Component owner,
                string path,
                float expected)
            {
                float actual = Float(owner, path);
                if (!Near(actual, expected))
                    Fail(owner.GetType().FullName + "." + path
                         + " differs from Movement Profile value "
                         + F(expected) + ".");
            }

            private static SerializedProperty Arr(
                Component owner, string path, int length)
            {
                SerializedProperty array = P(owner, path);
                if (!array.isArray || array.arraySize != length)
                    Fail(owner.GetType().FullName + "." + path + " must have length "
                         + length + ".");
                return array;
            }

            private static void ValidateReferences(
                SerializedProperty array,
                params UnityEngine.Object[] expected)
            {
                if (!array.isArray || array.arraySize != expected.Length)
                    Fail(array.propertyPath + " has the wrong reference count.");
                for (int index = 0; index < expected.Length; index++)
                    if (array.GetArrayElementAtIndex(index).objectReferenceValue
                        != expected[index])
                        Fail(array.propertyPath + " differs at index " + index + ".");
            }

            private static void Exact(
                SerializedProperty owner,
                string name,
                float expected,
                string label)
            {
                if (!Near(Rel(owner, name).floatValue, expected))
                    Fail(label + " " + name + " differs from " + F(expected) + ".");
            }

            private static void RequireCurve(
                SerializedProperty curve, string label)
            {
                SerializedProperty keys = Rel(curve, "m_Curve");
                if (!keys.isArray || keys.arraySize < 2)
                    Fail("LiteLoco " + label + " is missing its curve keys.");
            }

            private void RequirePersistent(UnityEngine.Object value, string label)
            {
                if (value == null || !EditorUtility.IsPersistent(value)
                    || string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(value)))
                    Fail(label + " must be a persistent project asset.");
                TrackAsset(value);
            }

            private static bool Near(float left, float right, float tolerance = Epsilon)
            {
                return Finite(left) && Finite(right)
                       && Mathf.Abs(left - right) <= tolerance;
            }

            private static bool Near(
                Vector3 left, Vector3 right, float tolerance = Epsilon)
            {
                return Finite(left) && Finite(right)
                       && Vector3.Distance(left, right) <= tolerance;
            }

            private static bool Near(
                Quaternion left, Quaternion right, float tolerance = Epsilon)
            {
                return Normalized(left) && Normalized(right)
                       && 1f - Mathf.Abs(Quaternion.Dot(left, right)) <= tolerance;
            }

            private static bool Normalized(Quaternion value)
            {
                if (!Finite(value.x) || !Finite(value.y)
                    || !Finite(value.z) || !Finite(value.w)) return false;
                float magnitude = Mathf.Sqrt(value.x * value.x + value.y * value.y
                                              + value.z * value.z + value.w * value.w);
                return Mathf.Abs(magnitude - 1f) <= 0.001f;
            }

            private static bool Finite(Vector3 value)
            {
                return Finite(value.x) && Finite(value.y) && Finite(value.z);
            }

            private static bool Finite(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }

            private static string F(float value)
            {
                return value.ToString("R", CultureInfo.InvariantCulture);
            }

            private static void AppendVector(StringBuilder text, Vector3 value)
            {
                text.Append(F(value.x)).Append(',').Append(F(value.y)).Append(',')
                    .Append(F(value.z));
            }

            private static void Fail(string message)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
