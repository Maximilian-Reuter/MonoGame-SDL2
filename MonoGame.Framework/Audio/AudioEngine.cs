#region License
/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2014 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 * See LICENSE for details.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.Xna.Framework.Audio
{
	// http://msdn.microsoft.com/en-us/library/dd940262.aspx
	public class AudioEngine : IDisposable
	{
		public const int ContentVersion = 46;

		private Dictionary<string, WaveBank> INTERNAL_waveBanks;

		private List<AudioCategory> INTERNAL_categories;
		private List<Variable> INTERNAL_variables;
		private Dictionary<long, RPC> INTERNAL_RPCs;
		private List<DSPParameter> INTERNAL_dspParameters;
		private Dictionary<long, DSPPreset> INTERNAL_dspPresets;

		public bool IsDisposed
		{
			get;
			private set;
		}

		public event EventHandler<EventArgs> Disposing;

		public AudioEngine(string settingsFile)
		{
			if (String.IsNullOrEmpty(settingsFile))
			{
				throw new ArgumentNullException("settingsFile");
			}

			using (Stream stream = TitleContainer.OpenStream(settingsFile))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				// Check the file header. Should be 'XGFS'
				if (reader.ReadUInt32() != 0x46534758)
				{
					throw new ArgumentException("XGFS format not recognized!");
				}

				// Check the Content and Tool versions
				if (reader.ReadUInt16() != ContentVersion)
				{
					throw new ArgumentException("XGFS Content version!");
				}
				if (reader.ReadUInt16() != 42)
				{
					throw new ArgumentException("XGFS Tool version!");
				}

				// Unknown value
				reader.ReadUInt16();

				// Last Modified, Unused
				reader.ReadUInt64();

				// Unknown value
				reader.ReadByte();

				// Number of AudioCategories
				ushort numCategories = reader.ReadUInt16();

				// Number of XACT Variables
				ushort numVariables = reader.ReadUInt16();

				// Unknown value, KEY#1 Length?
				reader.ReadUInt16();

				// Unknown value, KEY#2 Length?
				reader.ReadUInt16();

				// Number of RPC Variables
				ushort numRPCs = reader.ReadUInt16();

				// Number of DSP Presets/Parameters
				ushort numDSPPresets = reader.ReadUInt16();
				ushort numDSPParameters = reader.ReadUInt16();

				// Category Offset in XGS File
				uint categoryOffset = reader.ReadUInt32();

				// Variable Offset in XGS File
				uint variableOffset = reader.ReadUInt32();

				// Unknown value, KEY#1 Offset?
				reader.ReadUInt32();

				// Category Name Index Offset, unused
				reader.ReadUInt32();

				// Unknown value, KEY#2 Offset?
				reader.ReadUInt32();

				// Variable Name Index Offset, unused
				reader.ReadUInt32();

				// Category Name Offset in XGS File
				uint categoryNameOffset = reader.ReadUInt32();

				// Variable Name Offset in XGS File
				uint variableNameOffset = reader.ReadUInt32();

				// RPC Variable Offset in XGS File
				uint rpcOffset = reader.ReadUInt32();

				// DSP Preset/Parameter Offsets in XGS File
				uint dspPresetOffset = reader.ReadUInt32();
				uint dspParameterOffset = reader.ReadUInt32();

				// Obtain the Audio Category Names
				reader.BaseStream.Seek(categoryNameOffset, SeekOrigin.Begin);
				string[] categoryNames = new string[numCategories];
				for (int i = 0; i < numCategories; i += 1)
				{
					List<char> builtString = new List<char>();
					while (reader.PeekChar() != 0)
					{
						builtString.Add(reader.ReadChar());
					}
					reader.ReadChar(); // Null terminator
					categoryNames[i] = new string(builtString.ToArray());
				}

				// Obtain the Audio Categories
				reader.BaseStream.Seek(categoryOffset, SeekOrigin.Begin);
				INTERNAL_categories = new List<AudioCategory>();
				for (int i = 0; i < numCategories; i += 1)
				{
					// Maximum instances, Unused
					reader.ReadByte();

					// Fade In/Out, Unused
					reader.ReadUInt16();
					reader.ReadUInt16();

					// Instance Behavior Flags, Unused
					reader.ReadByte();

					// Unknown value
					reader.ReadUInt16();

					// Volume
					float volume = XACTCalculator.CalculateVolume(reader.ReadByte());

					// Visibility Flags, unused
					reader.ReadByte();

					// Add to the engine list
					INTERNAL_categories.Add(
						new AudioCategory(
							categoryNames[i],
							volume
						)
					);
				}

				// Obtain the Variable Names
				reader.BaseStream.Seek(variableNameOffset, SeekOrigin.Begin);
				string[] variableNames = new string[numVariables];
				for (int i = 0; i < numVariables; i += 1)
				{
					List<char> builtString = new List<char>();
					while (reader.PeekChar() != 0)
					{
						builtString.Add(reader.ReadChar());
					}
					reader.ReadChar(); // Null terminator
					variableNames[i] = new string(builtString.ToArray());
				}

				// Obtain the Variables
				reader.BaseStream.Seek(variableOffset, SeekOrigin.Begin);
				INTERNAL_variables = new List<Variable>();
				for (int i = 0; i < numVariables; i += 1)
				{
					// Variable Accessibility (See Variable constructor)
					byte varFlags = reader.ReadByte();

					// Variable Value, Boundaries
					float initialValue =	reader.ReadSingle();
					float minValue =	reader.ReadSingle();
					float maxValue =	reader.ReadSingle();

					// Add to the engine list
					INTERNAL_variables.Add(
						new Variable(
							variableNames[i],
							(varFlags & 0x01) != 0,
							(varFlags & 0x02) != 0,
							(varFlags & 0x04) == 0,
							(varFlags & 0x08) != 0,
							initialValue,
							minValue,
							maxValue
						)
					);
				}

				// Append built-in properties to Variable list
				bool hasVolume = false;
				foreach (Variable curVar in INTERNAL_variables)
				{
					if (curVar.Name.Equals("Volume"))
					{
						hasVolume = true;
					}
				}
				if (!hasVolume)
				{
					INTERNAL_variables.Add(
						new Variable(
							"Volume",
							true,
							false,
							false,
							false,
							1.0f,
							0.0f,
							1.0f
						)
					);
				}

				// Obtain the RPC Curves
				reader.BaseStream.Seek(rpcOffset, SeekOrigin.Begin);
				INTERNAL_RPCs = new Dictionary<long, RPC>();
				for (int i = 0; i < numRPCs; i += 1)
				{
					// RPC "Code", used by the SoundBanks
					long rpcCode = reader.BaseStream.Position;

					// RPC Variable
					ushort rpcVariable = reader.ReadUInt16();

					// Number of RPC Curve Points
					byte numPoints = reader.ReadByte();

					// RPC Parameter
					ushort rpcParameter = reader.ReadUInt16();

					// RPC Curve Points
					RPCPoint[] rpcPoints = new RPCPoint[numPoints];
					for (byte j = 0; j < numPoints; j += 1)
					{
						float x = reader.ReadSingle();
						float y = reader.ReadSingle();
						byte type = reader.ReadByte();
						rpcPoints[j] = new RPCPoint(
							x, y,
							(RPCPointType) type
						);
					}

					// Add to the engine list
					INTERNAL_RPCs.Add(
						rpcCode,
						new RPC(
							INTERNAL_variables[rpcVariable].Name,
							rpcParameter,
							rpcPoints
						)
					);
				}

				// Obtain the DSP Parameters
				reader.BaseStream.Seek(dspParameterOffset, SeekOrigin.Begin);
				INTERNAL_dspParameters = new List<DSPParameter>();
				for (int i = 0; i < numDSPParameters; i += 1)
				{
					// Effect Parameter Type
					byte type = reader.ReadByte();

					// Effect value, boundaries
					float value = reader.ReadSingle();
					float minVal = reader.ReadSingle();
					float maxVal = reader.ReadSingle();

					// Unknown value
					reader.ReadUInt16();

					// Add to Parameter list
					INTERNAL_dspParameters.Add(
						new DSPParameter(
							type,
							value,
							minVal,
							maxVal
						)
					);
				}

				// Obtain the DSP Presets
				reader.BaseStream.Seek(dspPresetOffset, SeekOrigin.Begin);
				INTERNAL_dspPresets = new Dictionary<long, DSPPreset>();
				int total = 0;
				for (int i = 0; i < numDSPPresets; i += 1)
				{
					// DSP "Code", used by the SoundBanks
					long dspCode = reader.BaseStream.Position;

					// Preset Accessibility
					bool global = (reader.ReadByte() == 1);

					// Number of preset parameters
					uint numParams = reader.ReadUInt32();

					// Obtain DSP Parameters
					DSPParameter[] parameters = new DSPParameter[numParams];
					for (uint j = 0; j < numParams; j += 1)
					{
						parameters[j] = INTERNAL_dspParameters[total];
						total += 1;
					}

					// Add to DSP Preset list
					// FIXME: Did XNA4 PC ever go past Reverb?
					INTERNAL_dspPresets.Add(
						dspCode,
						new DSPPreset(
							"Reverb",
							global,
							parameters
						)
					);
				}
			}

			// Create the WaveBank Dictionary
			INTERNAL_waveBanks = new Dictionary<string, WaveBank>();

			// Finally.
			IsDisposed = false;
		}

		public AudioEngine(
			string settingsFile,
			TimeSpan lookAheadTime,
			string rendererId
		) {
			throw new NotSupportedException();
		}

		~AudioEngine()
		{
			Dispose(true);
		}

		public void Dispose()
		{
			Dispose(false);
		}

		public void Dispose(bool disposing)
		{
			if (!IsDisposed)
			{
				if (Disposing != null)
				{
					Disposing.Invoke(this, null);
				}
				foreach (AudioCategory curCategory in INTERNAL_categories)
				{
					curCategory.Stop(AudioStopOptions.Immediate);
				}
				INTERNAL_categories.Clear();
				foreach (KeyValuePair<long, DSPPreset> curDSP in INTERNAL_dspPresets)
				{
					curDSP.Value.Dispose();
				}
				INTERNAL_dspPresets.Clear();
				INTERNAL_dspParameters.Clear();
				INTERNAL_variables.Clear();
				INTERNAL_RPCs.Clear();
				IsDisposed = true;
			}
		}

		public AudioCategory GetCategory(string name)
		{
			if (String.IsNullOrEmpty(name))
			{
				throw new ArgumentNullException("name");
			}
			for (int i = 0; i < INTERNAL_categories.Count; i += 1)
			{
				if (INTERNAL_categories[i].Name.Equals(name))
				{
					return INTERNAL_categories[i];
				}
			}
			throw new InvalidOperationException("Category not found!");
		}

		public float GetGlobalVariable(string name)
		{
			if (String.IsNullOrEmpty(name))
			{
				throw new ArgumentNullException("name");
			}
			for (int i = 0; i < INTERNAL_variables.Count; i += 1)
			{
				if (name.Equals(INTERNAL_variables[i].Name))
				{
					if (!INTERNAL_variables[i].IsGlobal)
					{
						throw new InvalidOperationException("Variable not global!");
					}
					return INTERNAL_variables[i].GetValue();
				}
			}
			throw new InvalidOperationException("Variable not found!");
		}

		public void SetGlobalVariable(string name, float value)
		{
			if (String.IsNullOrEmpty(name))
			{
				throw new ArgumentNullException("name");
			}
			for (int i = 0; i < INTERNAL_variables.Count; i += 1)
			{
				if (name.Equals(INTERNAL_variables[i].Name))
				{
					if (!INTERNAL_variables[i].IsGlobal)
					{
						throw new InvalidOperationException("Variable not global!");
					}
					INTERNAL_variables[i].SetValue(value);
					return; // We made it!
				}
			}
			throw new InvalidOperationException("Variable not found!");
		}

		public void Update()
		{
			// Update Global RPCs
			foreach (KeyValuePair<long, RPC> curRPC in INTERNAL_RPCs)
			foreach (Variable curVar in INTERNAL_variables)
			{
				if (curVar.Name.Equals(curRPC.Value.Variable) && curVar.IsGlobal)
				{
					if (curRPC.Value.Parameter == RPCParameter.Volume)
					{
						// TODO: Global volume?
					}
					else if (curRPC.Value.Parameter >= RPCParameter.NUM_PARAMETERS)
					{
						// FIXME: ASSUMPTION MANIA UP IN THIS BIZNAZZ
						foreach (KeyValuePair<long, DSPPreset> curDSP in INTERNAL_dspPresets)
						{
							if (curDSP.Value.Name.Equals("Reverb"))
							{
								curDSP.Value.SetParameter(
									(int) curRPC.Value.Parameter - (int) RPCParameter.NUM_PARAMETERS,
									GetGlobalVariable(curVar.Name)
								);
							}
						}
					}
					else
					{
						throw new Exception("RPC Parameter Type: " + curRPC.Value.Parameter);
					}
				}
			}

			// Update Cues
			foreach (AudioCategory curCategory in INTERNAL_categories)
			{
				curCategory.INTERNAL_update();
			}
		}

		internal void INTERNAL_addWaveBank(string name, WaveBank waveBank)
		{
			INTERNAL_waveBanks.Add(name, waveBank);
		}

		internal void INTERNAL_removeWaveBank(string name)
		{
			INTERNAL_waveBanks.Remove(name);
		}

		internal SoundEffect INTERNAL_getWaveBankTrack(string name, ushort track)
		{
			return INTERNAL_waveBanks[name].INTERNAL_getTrack(track);
		}

		internal string INTERNAL_getVariableName(ushort index)
		{
			return INTERNAL_variables[index].Name;
		}

		internal RPC INTERNAL_getRPC(uint code)
		{
			return INTERNAL_RPCs[code];
		}

		internal int INTERNAL_getDSP(uint code)
		{
			return INTERNAL_dspPresets[code].Handle;
		} 

		internal AudioCategory INTERNAL_initCue(Cue newCue, ushort category)
		{
			List<Variable> cueVariables = new List<Variable>();
			foreach (Variable curVar in INTERNAL_variables)
			{
				if (!curVar.IsGlobal)
				{
					cueVariables.Add(curVar.Clone());
				}
			}
			newCue.INTERNAL_genVariables(cueVariables);
			return INTERNAL_categories[category];
		}
	}
}
