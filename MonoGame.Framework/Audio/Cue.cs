using System;
using System.Collections.Generic;

namespace Microsoft.Xna.Framework.Audio
{
	// http://msdn.microsoft.com/en-us/library/microsoft.xna.framework.audio.cue.aspx
	public sealed class Cue : IDisposable
	{
		private AudioEngine INTERNAL_baseEngine;

		private CueData INTERNAL_data;
		private XACTSound INTERNAL_activeSound;
		private List<SoundEffectInstance> INTERNAL_instancePool;

		// User-controlled sounds require a bit more trickery.
		private bool INTERNAL_userControlledPlaying;
		private float INTERNAL_controlledValue;

		private bool INTERNAL_isPositional;
		private AudioListener INTERNAL_listener;
		private AudioEmitter INTERNAL_emitter;

		private List<Variable> INTERNAL_variables;

		private AudioCategory INTERNAL_category;
		private bool INTERNAL_isManaged;

		private bool INTERNAL_queuedPlayback;

		private static Random random = new Random();

		public bool IsCreated
		{
			get;
			private set;
		}

		public bool IsDisposed
		{
			get;
			private set;
		}

		public bool IsPaused
		{
			get
			{
				foreach (SoundEffectInstance sfi in INTERNAL_instancePool)
				{
					if (sfi.State == SoundState.Paused)
					{
						return true;
					}
				}
				return false;
			}
		}

		public bool IsPlaying
		{
			get
			{
				foreach (SoundEffectInstance sfi in INTERNAL_instancePool)
				{
					if (sfi.State != SoundState.Stopped)
					{
						return true;
					}
				}
				return false;
			}
		}

		public bool IsPrepared
		{
			get;
			private set;
		}

		public bool IsPreparing
		{
			get;
			private set;
		}

		public bool IsStopped
		{
			get
			{
				return !IsPlaying;
			}
		}

		public bool IsStopping
		{
			get
			{
				// FIXME: Authored Stop Options?
				return false;
			}
		}

		public string Name
		{
			get;
			private set;
		}

		public event EventHandler<EventArgs> Disposing;

		internal Cue(
			AudioEngine audioEngine,
			List<string> waveBankNames,
			string name,
			CueData data,
			bool managed
		) {
			INTERNAL_baseEngine = audioEngine;

			Name = name;

			INTERNAL_data = data;
			foreach (XACTSound curSound in data.Sounds)
			{
				if (!curSound.HasLoadedTracks)
				{
					curSound.LoadTracks(
						INTERNAL_baseEngine,
						waveBankNames
					);
				}
			}

			INTERNAL_isManaged = managed;

			INTERNAL_category = INTERNAL_baseEngine.INTERNAL_initCue(
				this,
				data.Category
			);

			INTERNAL_userControlledPlaying = false;
			INTERNAL_isPositional = false;
			INTERNAL_queuedPlayback = false;

			INTERNAL_instancePool = new List<SoundEffectInstance>();
		}

		~Cue()
		{
			Dispose();
		}

		public void Apply3D(AudioListener listener, AudioEmitter emitter)
		{
			if (IsPlaying && !INTERNAL_isPositional)
			{
				throw new InvalidOperationException("Apply3D call after Play!");
			}
			if (listener == null)
			{
				throw new ArgumentNullException("listener");
			}
			if (emitter == null)
			{
				throw new ArgumentNullException("emitter");
			}
			INTERNAL_listener = listener;
			INTERNAL_emitter = emitter;
			SetVariable(
				"Distance",
				Vector3.Distance(
					INTERNAL_emitter.Position,
					INTERNAL_listener.Position
				)
			);
			// TODO: All Internal 3D Audio Variables
			INTERNAL_isPositional = true;
		}

		public void Dispose()
		{
			if (!IsDisposed)
			{
				if (Disposing != null)
				{
					Disposing.Invoke(this, null);
				}
				if (INTERNAL_instancePool != null)
				{
					foreach (SoundEffectInstance sfi in INTERNAL_instancePool)
					{
						sfi.Dispose();
					}
					INTERNAL_instancePool.Clear();
					INTERNAL_queuedPlayback = false;
				}
				IsDisposed = true;
			}
		}

		public float GetVariable(string name)
		{
			if (String.IsNullOrEmpty(name))
			{
				throw new ArgumentNullException("name");
			}
			foreach (Variable curVar in INTERNAL_variables)
			{
				if (name.Equals(curVar.Name))
				{
					return curVar.GetValue();
				}
			}
			throw new Exception("Instance variable not found!");
		}

		public void Pause()
		{
			if (!IsPlaying)
			{
				return;
			}
			foreach (SoundEffectInstance sfi in INTERNAL_instancePool)
			{
				sfi.Pause();
			}
		}

		public void Play()
		{
			if (IsPlaying)
			{
				throw new InvalidOperationException("Cue already playing!");
			}

			INTERNAL_category.INTERNAL_initCue(this);

			if (GetVariable("NumCueInstances") >= INTERNAL_data.InstanceLimit)
			{
				if (INTERNAL_data.MaxCueBehavior == CueData.MaxInstanceBehavior.Fail)
				{
					// What we just did should be undone. Oops.
					INTERNAL_category.INTERNAL_removeLatestCue();
					return; // Just ignore us...
				}
				else if (INTERNAL_data.MaxCueBehavior == CueData.MaxInstanceBehavior.Queue)
				{
					throw new Exception("Cue Queueing not handled!");
				}
				else if (INTERNAL_data.MaxCueBehavior == CueData.MaxInstanceBehavior.ReplaceOldest)
				{
					INTERNAL_category.INTERNAL_removeOldestCue(Name);
				}
				else if (INTERNAL_data.MaxCueBehavior == CueData.MaxInstanceBehavior.ReplaceQuietest)
				{
					INTERNAL_category.INTERNAL_removeQuietestCue(Name);
				}
				else if (INTERNAL_data.MaxCueBehavior == CueData.MaxInstanceBehavior.ReplaceLowestPriority)
				{
					// FIXME: Priority?
					INTERNAL_category.INTERNAL_removeOldestCue(Name);
				}
			}

			INTERNAL_category.INTERNAL_addCue(this);

			if (!INTERNAL_calculateNextSound())
			{
				return;
			}

			INTERNAL_setupSounds();

			if (INTERNAL_isPositional)
			{
				foreach (SoundEffectInstance sfi in INTERNAL_instancePool)
				{
					sfi.Apply3D(
						INTERNAL_listener,
						INTERNAL_emitter
					);
				}
			}

			INTERNAL_queuedPlayback = true;
		}

		public void Resume()
		{
			if (!IsPaused)
			{
				return;
			}
			foreach (SoundEffectInstance sfi in INTERNAL_instancePool)
			{
				sfi.Resume();
			}
		}

		public void SetVariable(string name, float value)
		{
			if (String.IsNullOrEmpty(name))
			{
				throw new ArgumentNullException("name");
			}
			foreach (Variable curVar in INTERNAL_variables)
			{
				if (name.Equals(curVar.Name))
				{
					curVar.SetValue(value);
					return;
				}
			}
			throw new Exception("Instance variable not found!");
		}

		public void Stop(AudioStopOptions options)
		{
			if (INTERNAL_queuedPlayback)
			{
				INTERNAL_queuedPlayback = false;
				return;
			}
			foreach (SoundEffectInstance sfi in INTERNAL_instancePool)
			{
				sfi.Stop();
			}
			INTERNAL_userControlledPlaying = false;
		}

		internal void INTERNAL_startPlayback()
		{
			if (INTERNAL_queuedPlayback)
			{
				INTERNAL_queuedPlayback = false;
				foreach (SoundEffectInstance sfi in INTERNAL_instancePool)
				{
					sfi.Play();
				}
			}
		}

		internal bool INTERNAL_checkActive()
		{
			if (IsStopped && !INTERNAL_queuedPlayback && !INTERNAL_userControlledPlaying)
			{
				if (INTERNAL_isManaged)
				{
					Dispose();
				}
				return false;
			}
			return true;
		}

		internal void INTERNAL_update()
		{
			for (int i = 0; i < INTERNAL_instancePool.Count; i++)
			{
				if (INTERNAL_instancePool[i].State == SoundState.Stopped)
				{
					INTERNAL_instancePool[i].Dispose();
					INTERNAL_instancePool.RemoveAt(i);
					i--;
				}
			}

			// User control updates
			if (INTERNAL_data.IsUserControlled)
			{
				if (	INTERNAL_userControlledPlaying &&
					INTERNAL_controlledValue != GetVariable(INTERNAL_data.UserControlVariable)	)
				{
					// TODO: Crossfading
					Stop(AudioStopOptions.Immediate);
					if (!INTERNAL_calculateNextSound())
					{
						// Nothing to play, bail.
						return;
					}
					INTERNAL_setupSounds();
					foreach (SoundEffectInstance sfi in INTERNAL_instancePool)
					{
						sfi.Play();
					}
				}

				if (INTERNAL_activeSound == null)
				{
					return;
				}
			}

			if (INTERNAL_isPositional)
			{
				foreach (SoundEffectInstance sfi in INTERNAL_instancePool)
				{
					sfi.Apply3D(
						INTERNAL_listener,
						INTERNAL_emitter
					);
				}
			}

			float rpcVolume = 1.0f;
			foreach (uint curCode in INTERNAL_activeSound.RPCCodes)
			{
				RPC curRPC = INTERNAL_baseEngine.INTERNAL_getRPC(curCode);
				float result;
				try
				{
					result = curRPC.CalculateRPC(GetVariable(curRPC.Variable));
				}
				catch
				{
					// It's a global variable we're looking for!
					result = curRPC.CalculateRPC(
						INTERNAL_baseEngine.GetGlobalVariable(
							curRPC.Variable
						)
					);
				}
				if (curRPC.Parameter == RPCParameter.Volume)
				{
					rpcVolume *= 1.0f + (result / 10000.0f);
				}
				else
				{
					throw new Exception("RPC Parameter Type: " + curRPC.Parameter);
				}
			}
			foreach (SoundEffectInstance sfi in INTERNAL_instancePool)
			{
				/* The final volume should be the combination of the
				 * authored volume, Volume variable and RPC volume result.
				 */
				sfi.Volume = INTERNAL_activeSound.Volume * GetVariable("Volume") * rpcVolume;
			}
		}

		internal void INTERNAL_genVariables(List<Variable> cueVariables)
		{
			INTERNAL_variables = cueVariables;
		}

		private bool INTERNAL_calculateNextSound()
		{
			INTERNAL_activeSound = null;

			// Pick a sound based on a Cue instance variable
			if (INTERNAL_data.IsUserControlled)
			{
				INTERNAL_userControlledPlaying = true;
				INTERNAL_controlledValue = GetVariable(
					INTERNAL_data.UserControlVariable
				);
				for (int i = 0; i < INTERNAL_data.Probabilities.Length / 2; i++)
				{
					if (	INTERNAL_controlledValue <= INTERNAL_data.Probabilities[i, 0] &&
						INTERNAL_controlledValue >= INTERNAL_data.Probabilities[i, 1]	)
					{
						INTERNAL_activeSound = INTERNAL_data.Sounds[i];
						return true;
					}
				}

				/* This should only happen when the
				 * UserControlVariable is none of the sound
				 * probabilities, in which case we are just
				 * silent. But, we are still claiming to be
				 * "playing" in the meantime.
				 * -flibit
				 */
				return false;
			}

			// Randomly pick a sound
			double max = 0.0;
			for (int i = 0; i < INTERNAL_data.Probabilities.GetLength(0); i++)
			{
				max += INTERNAL_data.Probabilities[i, 0] - INTERNAL_data.Probabilities[i, 1];
			}
			double next = random.NextDouble() * max;

			for (int i = INTERNAL_data.Probabilities.GetLength(0) - 1; i >= 0; i--)
			{
				if (next > max - (INTERNAL_data.Probabilities[i, 0] - INTERNAL_data.Probabilities[i, 1]))
				{
					INTERNAL_activeSound = INTERNAL_data.Sounds[i];
					break;
				}
				max -= INTERNAL_data.Probabilities[i, 0] - INTERNAL_data.Probabilities[i, 1];
			}

			return true;
		}

		private void INTERNAL_setupSounds()
		{
			INTERNAL_activeSound.GenerateInstances(INTERNAL_instancePool);

			foreach (uint curDSP in INTERNAL_activeSound.DSPCodes)
			{
				int handle = INTERNAL_baseEngine.INTERNAL_getDSP(curDSP);
				foreach (SoundEffectInstance sfi in INTERNAL_instancePool)
				{
					// FIXME: This only applies the last DSP!
					sfi.INTERNAL_applyEffect(handle);
				}
			}
		}
	}
}
