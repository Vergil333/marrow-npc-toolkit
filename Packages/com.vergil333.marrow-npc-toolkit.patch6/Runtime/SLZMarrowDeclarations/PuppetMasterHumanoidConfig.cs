// Reference type only — never authored, only pointed at.
//
// PuppetMaster declares a `humanoidConfig` field of this type. It is null on
// the stock NPC as well, so nothing here assigns one; the type exists purely so
// the field can be declared and the serialized layout matches.

namespace SLZ.Marrow.PuppetMasta
{
    public class PuppetMasterHumanoidConfig : UnityEngine.ScriptableObject
    {
    }
}
