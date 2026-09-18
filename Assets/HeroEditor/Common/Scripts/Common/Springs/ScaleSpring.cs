using UnityEngine;

namespace Assets.HeroEditor.Common.Scripts.Common.Springs
{
    /// <summary>
    /// Changes target scale like spring.
    /// </summary>
    public class ScaleSpring : SpringBase
    {
        public float From;
        public float To;
        public float Dumping;

        private Vector3 _scale;
        private float _amplitude = 1;

        public static void Begin(Component target, float from, float to, float speed, float dumping)
        {
            var component = target.GetComponent<ScaleSpring>() ?? target.gameObject.AddComponent<ScaleSpring>();

            component.From = from;
            component.To = to;
            component.Speed = speed;
            component.Dumping = dumping;
            component.enabled = true;
        }

        protected override void OnUpdate()
        {
            _amplitude = Mathf.Max(0, _amplitude - Dumping * UnityEngine.Time.deltaTime);

            // Project edit: the spring used to write back the scale it captured when the hit landed,
            // facing included. A unit turned during the squash (the fight ending and standing it down,
            // a target crossing behind it) snapped back to the old facing on the next frame, and a
            // hero hit facing left as the fight ended stood facing left through the whole setup screen.
            // Only the magnitude is the spring's to animate; the sign is whoever turned the unit last.
            transform.localScale = KeepFacing(_scale * (From + (To - From) * Sin() * _amplitude));
     
            if (_amplitude <= 0)
            {
                enabled = false;
            }
        }

        public override void OnEnable()
        {
            _scale = transform.localScale;
            base.OnEnable();
            Reset();
        }

        public void OnDisable()
        {
            transform.localScale = KeepFacing(_scale);
        }

        private Vector3 KeepFacing(Vector3 scale)
        {
            scale.x = Mathf.Abs(scale.x) * (transform.localScale.x < 0 ? -1 : 1);
            return scale;
        }

        public void Reset()
        {
            _amplitude = 1;
        }
    }
}