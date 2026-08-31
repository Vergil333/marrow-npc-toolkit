// Serialized layout only — the game's own classes supply the behaviour.
//
// `MarrowBody._defaultRigidbodyInfo` and `MarrowJoint._defaultConfigJointInfo`
// are snapshots of a body's and a joint's authored state. Restoring them is
// what a pool does when it hands an object out, which is why their absence
// showed up as NullReferenceExceptions in `PuppetMaster.OnPoolInitialize()` and
// `OnPoolSpawn()` rather than anywhere earlier: nothing reads them until a
// spawn asks for a reset.
//
// Our stubs declared neither field, so the structs were missing from our
// serialized data entirely and arrived null. Field names, order and types are
// read off the stock NPC's typetree (peasantfemaleb.bundle). The container type
// names are not recoverable from a typetree — they are inferred from the field
// names that reference them (`slerpDriveExt` -> JointDriveExt, and so on).
// Unity matches serialized data field by field, so the layout is what has to be
// right.

using System;
using UnityEngine;

namespace SLZ.Marrow.Interaction
{
    [Serializable]
    public class RigidbodyInfo
    {
        public float mass;
        public float drag;
        public float angularDrag;
        public bool useGravity;
        public bool isKinematic;
        public bool detectCollisions;
        public bool interpolate;
        public int collisionDetection;
        public int constraints;
        public Vector3 centerOfMass;
        public Vector3 inertiaTensor;
        public Quaternion inertiaTensorRotation;
        public Vector3 initalVelocity;   // SLZ's spelling, kept so the name matches
        public Vector3 initialAngularVelocity;
    }

    [Serializable]
    public class JointDriveExt
    {
        public float positionSpring;
        public float positionDamper;
        public float maximumForce;
    }

    [Serializable]
    public class SoftJointLimitExt
    {
        public float limit;
        public float bounciness;
        public float contactDistance;
    }

    [Serializable]
    public class SoftJointLimitSpringExt
    {
        public float spring;
        public float damper;
    }

    [Serializable]
    public class ConfigJointInfo
    {
        public Quaternion startRotation;
        public Vector3 axis;
        public Vector3 secondaryAxis;
        public Vector3 anchor;
        public Vector3 connectedAnchor;
        public bool autoConfigureConnectedAnchor;
        public float breakForce;
        public float breakTorque;
        public bool enableCollision;
        public bool enablePreprocessing;
        public float massScale;
        public float connectedMassScale;
        public float projectionAngle;
        public float projectionDistance;
        public int projectionModeExt;
        public JointDriveExt slerpDriveExt;
        public JointDriveExt angularYZDriveExt;
        public JointDriveExt angularXDriveExt;
        public int rotationDriveMode;
        public Vector3 targetAngularVelocity;
        public Quaternion targetRotation;
        public JointDriveExt zDriveExt;
        public JointDriveExt yDriveExt;
        public JointDriveExt xDriveExt;
        public Vector3 targetVelocity;
        public Vector3 targetPosition;
        public SoftJointLimitExt angularZLimitExt;
        public SoftJointLimitExt angularYLimitExt;
        public SoftJointLimitExt highAngularXLimitExt;
        public SoftJointLimitExt lowAngularXLimitExt;
        public SoftJointLimitExt linearLimitExt;
        public SoftJointLimitSpringExt angularYZLimitSpringExt;
        public SoftJointLimitSpringExt angularXLimitSpringExt;
        public SoftJointLimitSpringExt linearLimitSpringExt;
        public int angularZMotion;
        public int angularYMotion;
        public int angularXMotion;
        public int zMotion;
        public int yMotion;
        public int xMotion;
        public bool configuredInWorldSpace;
        public bool swapBodies;
    }
}
