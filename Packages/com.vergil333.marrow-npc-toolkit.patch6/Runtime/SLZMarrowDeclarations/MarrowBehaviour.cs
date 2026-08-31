// Reference type only. MarrowEntity._behaviours is a vector of
// PPtr<$MarrowBehaviour>, so the array has to be declared with this element type
// for the serialized layout to match — we never author an instance, only an
// empty array. Declaring it as MonoBehaviour[] instead produced a differently
// typed field that a top-level type comparison cannot see, because both read as
// "vector".
namespace SLZ.Marrow.Interaction
{
    public class MarrowBehaviour : UnityEngine.MonoBehaviour
    {
    }
}
