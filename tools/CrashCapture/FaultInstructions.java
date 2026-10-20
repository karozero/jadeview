// Prints bounded instructions from a caller-supplied memory snapshot.
// @category JadeView.Diagnostics
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Instruction;
import ghidra.app.decompiler.DecompInterface;

public class FaultInstructions extends GhidraScript {
    public void run() throws Exception {
        String[] args = getScriptArgs();
        boolean decompile = args.length > 1 && args[args.length - 1].equalsIgnoreCase("decompile");
        int limit = decompile ? args.length - 1 : args.length;
        for (int index = 0; index < limit; index++) {
            Address start = toAddr(args[index]);
            println("=== " + start + " ===");
            disassemble(start);
            if (decompile) {
                var function = getFunctionContaining(start);
                if (function == null) function = createFunction(start, null);
                if (function == null) {
                    println("No function at " + start);
                    continue;
                }
                println("Function " + function.getEntryPoint());
                var decompiler = new DecompInterface();
                try {
                    decompiler.openProgram(currentProgram);
                    var result = decompiler.decompileFunction(function, 30, monitor);
                    if (result.decompileCompleted()) println(result.getDecompiledFunction().getC());
                    else println(result.getErrorMessage());
                } finally { decompiler.dispose(); }
                continue;
            }
            Instruction instruction = getInstructionAt(start);
            for (int instructionIndex = 0; instruction != null && instructionIndex < 80; instructionIndex++) {
                println(instruction.getAddress() + " " + instruction.toString());
                instruction = instruction.getNext();
            }
        }
    }
}
