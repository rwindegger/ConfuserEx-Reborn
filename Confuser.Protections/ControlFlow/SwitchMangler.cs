﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using dnlib.DotNet.Emit;
using Confuser.Core.Services;
using dnlib.DotNet;

namespace Confuser.Protections.ControlFlow
{
    class SwitchMangler : ManglerBase
    {
        LinkedList<Instruction[]> SpiltStatements(InstrBlock block, MethodTrace trace, CFContext ctx)
        {
            LinkedList<Instruction[]> statements = new LinkedList<Instruction[]>();
            List<Instruction> currentStatement = new List<Instruction>();

            foreach (var instr in block.Instructions)
            {
                currentStatement.Add(instr);

                if (trace.StackDepths[trace.OffsetToIndexMap(instr.Offset)] == 0)
                {
                    statements.AddLast(currentStatement.ToArray());
                    currentStatement.Clear();
                }
            }

            if (currentStatement.Count > 0)
                statements.AddLast(currentStatement.ToArray());

            return statements;
        }

        public override void Mangle(CilBody body, ScopeBlock root, CFContext ctx)
        {
            MethodTrace trace = ctx.Context.Registry.GetService<ITraceService>().Trace(ctx.Method);

            body.MaxStack++;
            foreach (var block in GetAllBlocks(root))
            {
                var statements = SpiltStatements(block, trace, ctx);

                // Make sure .ctor is executed before switch
                if (ctx.Method.IsInstanceConstructor)
                {
                    List<Instruction> newStatement = new List<Instruction>();
                    while (statements.First != null)
                    {
                        newStatement.AddRange(statements.First.Value);
                        Instruction lastInstr = statements.First.Value.Last();
                        statements.RemoveFirst();
                        if (lastInstr.OpCode == OpCodes.Call && ((IMethod)lastInstr.Operand).Name == ".ctor")
                            break;
                    }
                    statements.AddFirst(newStatement.ToArray());
                }

                if (statements.Count < 3) continue;

                int[] key = Enumerable.Range(0, statements.Count).ToArray();
                ctx.Random.Shuffle(key);

                Instruction switchInstr = new Instruction(OpCodes.Switch);

                var switchHdr = new List<Instruction>() {
                    Instruction.CreateLdcI4(key[1]),
                    switchInstr
                };
                ctx.AddJump(switchHdr, statements.Last.Value[0]);
                ctx.AddJunk(switchHdr);

                var current = statements.First;
                int i = 0;
                Instruction[] operands = new Instruction[statements.Count];
                while (current.Next != null)
                {
                    List<Instruction> newStatement = new List<Instruction>(current.Value);
                    if (i != 0)
                    {
                        newStatement.Add(Instruction.CreateLdcI4(key[i + 1]));
                        ctx.AddJump(newStatement, switchInstr);
                        ctx.AddJunk(newStatement);
                        operands[key[i]] = current.Value[0];
                    }
                    else
                        operands[key[i]] = switchHdr[0];

                    current.Value = newStatement.ToArray();
                    current = current.Next;
                    i++;
                }
                operands[key[i]] = current.Value[0];
                switchInstr.Operand = operands;

                Instruction[] first = statements.First.Value;
                statements.RemoveFirst();
                Instruction[] last = statements.Last.Value;
                statements.RemoveLast();

                var newStatements = statements.ToList();
                ctx.Random.Shuffle(newStatements);

                block.Instructions.Clear();
                block.Instructions.AddRange(first);
                block.Instructions.AddRange(switchHdr);
                foreach (var statement in newStatements)
                    block.Instructions.AddRange(statement);
                block.Instructions.AddRange(last);
            }
        }
    }
}