/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.SmartAssembly {
	class ProxyCallFixer : ProxyCallFixer1 {
		static readonly Dictionary<char, int> specialCharsDict = new Dictionary<char, int>();
		static readonly char[] specialChars = new char[] {
					'\u007f','\u0089','\u001e','\u0017','\u008b','\u0092','\u0095','\u0093',
					'\u0010','\u0004','\u0081','\u009a','\u009c','\u0097','\u001b','\u0088',
					'\u009d','\u008a','\a','\u0019','\u008f','\u0086','\u009e','\u001d',
					'\u008d','\u0099','\u0098','\b','\u0082','\u0096','\u0018','\u0016',
					'\u0003','\u0014','\u000f','\u0090','\u009f','\u0080','\u008e','\u0087',
					'\u0015','\u0001','\u001c','\u0005','\u008c','\u0011','\u0094','\u0012',
					'\u001a','\u000e','\u0013','\u009b','\u0083','\u0006','\u001f','\u0091',
					'\u0084','\u0002'
		};

		ISimpleDeobfuscator simpleDeobfuscator;

		static ProxyCallFixer() {
			for (int i = 0; i < specialChars.Length; i++)
				specialCharsDict[specialChars[i]] = i;
		}

		public ProxyCallFixer(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator)
			: base(module) => this.simpleDeobfuscator = simpleDeobfuscator;

		protected override object CheckCctor(ref TypeDef type, MethodDef cctor) {
			var instrs = cctor.Body.Instructions;
			if (instrs.Count > 10)
				return null;
			if (instrs.Count != 3)
				simpleDeobfuscator.Deobfuscate(cctor);
			if (instrs.Count != 3)
				return null;
			if (!instrs[0].IsLdcI4())
				return null;
			if (instrs[1].OpCode != OpCodes.Call || !IsDelegateCreatorMethod(instrs[1].Operand as MethodDef))
				return null;
			if (instrs[2].OpCode != OpCodes.Ret)
				return null;

			int delegateToken = 0x02000001 + instrs[0].GetLdcI4Value();
			if (type.MDToken.ToInt32() != delegateToken) {
				Logger.w("Delegate token is not current type");
				return null;
			}

			return new object();
		}

		protected override void GetCallInfo(object context, FieldDef field, out IMethod calledMethod, out OpCode callOpcode) {
			callOpcode = OpCodes.Call;
			string name = field.Name.String;

			uint memberRefRid = 0;
			for (int i = name.Length - 1; i >= 0; i--) {
				char c = name[i];
				if (c == '~') {
					callOpcode = OpCodes.Callvirt;
					break;
				}

				if (specialCharsDict.TryGetValue(c, out int val))
					memberRefRid = memberRefRid * (uint)specialChars.Length + (uint)val;
			}
			memberRefRid++;

			calledMethod = module.ResolveMemberRef(memberRefRid);
			if (calledMethod == null)
				Logger.w("Ignoring invalid method RID: {0:X8}, field: {1:X8}", memberRefRid, field.MDToken.ToInt32());
		}

		public void FindDelegateCreator(ModuleDefMD module) {
			var callCounter = new CallCounter();
			foreach (var type in module.Types) {
				if (type.Namespace != "" || !DotNetUtils.DerivesFromDelegate(type))
					continue;
				var cctor = type.FindStaticConstructor();
				if (cctor == null)
					continue;
				foreach (var method in DotNetUtils.GetMethodCalls(cctor))
					callCounter.Add(method);
			}

			var mostCalls = callCounter.Most();
			if (mostCalls == null)
				return;

			SetDelegateCreatorMethod(DotNetUtils.GetMethod(module, mostCalls));
		}
	}
}
