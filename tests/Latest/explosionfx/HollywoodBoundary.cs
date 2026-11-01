#if HFX_PRESENT
// Explicit renderer doubles shaped from HollywoodFX 2.0.0 public source.
// They model private pooled emitters and deferred emission, not its GPU shader
// or confinement calculations. No HollywoodFX code/assets are redistributed.
using System.Runtime.CompilerServices;
using UnityEngine;
namespace HollywoodFX.Particles {
 public class Emitter(ParticleSystem main) {
  public readonly ParticleSystem Main=main;
  public void Emit(Vector3 position,float scale){Main.transform.position=position;Main.transform.localScale=Vector3.one*scale;Main.Play(true);}
 }
 public class EffectBundle(Emitter[] emitters) {public readonly Emitter[] Emitters=emitters;public void Emit(Vector3 position,float scale){foreach(var emitter in Emitters)emitter.Emit(position,scale);}}
}
namespace HollywoodFX.Explosion {
 using HollywoodFX.Particles;
 public class Blast(EffectBundle[] effectsUp,EffectBundle[] effectsAngled,float scale) {
  [MethodImpl(MethodImplOptions.NoInlining)] public void Emit(Vector3 position,Vector3 normal){foreach(var bundle in effectsUp)bundle.Emit(position,scale);foreach(var bundle in effectsAngled)bundle.Emit(position,scale);}
 }
 public class ConfinedBlast(float radius,EffectBundle[] misc,EffectBundle dust) {
  private Vector3 _position;public float AuthoredRadius=>radius;
  [MethodImpl(MethodImplOptions.NoInlining)]public void Emit(Vector3 position,Vector3 normal){_position=position;}
  public void CompleteDeferred(){foreach(var bundle in misc)bundle.Emit(_position,1);dust.Emit(_position,1);}
 }
}
#endif
