using System.Collections;
using System.Linq;
using Assets.HeroEditor.Common.Scripts.CharacterScripts.Firearms.Enums;
using UnityEngine;

namespace Assets.HeroEditor.Common.Scripts.CharacterScripts.Firearms
{
    /// <summary>
    /// Firearm reload process.
    /// </summary>
    public class FirearmReload : MonoBehaviour
    {
        public Character Character;
        public Firearm Firearm;
        public AudioSource AudioSource;

        /// <summary>
        /// Should be set outside (by input manager or AI).
        /// </summary>
        [HideInInspector] public bool ReloadButtonDown;
        [HideInInspector] public bool Reloading;

        public void Update()
        {
            if (ReloadButtonDown && !Reloading && Firearm.AmmoShooted > 0)
            {
                StartCoroutine(Reload());
            }
        }

        public IEnumerator Reload()
        {
            Reloading = true;
            Character.Animator.SetBool("Reloading", true);

            yield return null;

            var stateInfo = Character.Animator.GetNextAnimatorStateInfo(0);
            var clip = Firearm.Params.ReloadAnimation;
            var duration = (Firearm.Params.MagazineType == MagazineType.Removable ? clip.length : clip.length * Firearm.AmmoShooted) / stateInfo.speed / stateInfo.speedMultiplier;

            switch (Firearm.Params.LoadType)
            {
	            case FirearmLoadType.Drum:
		            for (var i = 0; i < Firearm.AmmoShooted; i++)
		            {
			            Firearm.Fire.CreateShell();
		            }

		            break;
	            case FirearmLoadType.Lamp:
		            Firearm.Fire.SetLamp(Firearm.Params.GetColorFromMeta("LampReload"));
					break;
            }

            if (Firearm.Params.Type == FirearmType.Crossbow)
            {
                yield return new WaitForSeconds(duration / 2);

                Character.FirearmsRenderers.Single(i => i.name == "Rifle").sprite = Character.Firearms.Single(i => i.name == "Rifle");

                yield return new WaitForSeconds(duration / 2);
            }
            else
            {
                yield return new WaitForSeconds(duration);
            }

            if (Firearm.Params.LoadType == FirearmLoadType.Lamp)
	        {
		        Firearm.Fire.SetLamp(Firearm.Params.GetColorFromMeta("LampReady"));
			}

            Firearm.AmmoShooted = 0;
            Character.Animator.SetBool("Reloading", false);
            Character.Animator.SetInteger("HoldType", (int) Firearm.Params.HoldType);
            Reloading = false;
        }

        public void PlayAudioEffect()
        {
            AudioSource.Play();
        }
    }
}