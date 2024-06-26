﻿using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Autodesk.Revit.Attributes;

namespace RpsRuntime
{
    /// <summary>
    /// The ExternalCommandAssemblyBuilder creates an assembly (.net dll) for
    /// a list of python scripts that can be used as IExternalCommand implementations
    /// in the Revit user interface (PushButtonData).
    /// </summary>
    public class ExternalCommandAssemblyBuilder
    {
        /// <summary>
        /// Build a new assembly and save it to disk as "pathToDll". Create a type (implementing IExternalCommand) for
        /// each class name in classNamesToScriptPaths that, when "Execute()" is called on it, will load the corresponding python script
        /// from the disk and execute it.
        /// </summary>
        public void BuildExternalCommandAssembly(string pathToDll, IDictionary<string, string> classNamesToScriptPaths)
        {
            var dllName = Path.GetFileNameWithoutExtension(pathToDll);
            var dllFolder = Path.GetDirectoryName(pathToDll);
            var assemblyName = new AssemblyName { Name = dllName + ".dll", Version = new Version(1, 0, 0, 0) };
            var assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndSave, dllFolder);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule(dllName + "Module", dllName + ".dll");

            foreach (var className in classNamesToScriptPaths.Keys)
            {
                var typeBuilder = moduleBuilder.DefineType(className,
                                                        TypeAttributes.Class | TypeAttributes.Public,
                                                        typeof(RpsExternalCommandScriptBase));

                // add RegenerationAttribute to type
                var regenerationConstructorInfo = typeof(RegenerationAttribute).GetConstructor([typeof(RegenerationOption)
                ]);
                var regenerationAttributeBuilder = new CustomAttributeBuilder(regenerationConstructorInfo!,
                    [RegenerationOption.Manual]);
                typeBuilder.SetCustomAttribute(regenerationAttributeBuilder);

                // add TransactionAttribute to type
                var transactionConstructorInfo = typeof(TransactionAttribute).GetConstructor([typeof(TransactionMode)]);
                var transactionAttributeBuilder = new CustomAttributeBuilder(transactionConstructorInfo!,
                    [TransactionMode.Manual]);
                typeBuilder.SetCustomAttribute(transactionAttributeBuilder);

                // call base constructor with a script path
                var ci = typeof(RpsExternalCommandScriptBase).GetConstructor([typeof(string)]);

                var constructorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new Type[0]);
                var gen = constructorBuilder.GetILGenerator();
                gen.Emit(OpCodes.Ldarg_0);                // Load "this" onto eval stack
                gen.Emit(OpCodes.Ldstr, classNamesToScriptPaths[className]);  // Load the path to the command as a string onto stack
                gen.Emit(OpCodes.Call, ci!);               // call base constructor (consumes "this" and the string)
                gen.Emit(OpCodes.Nop);                    // Fill some space - this is how it is generated for equivalent C# code
                gen.Emit(OpCodes.Nop);
                gen.Emit(OpCodes.Nop);
                gen.Emit(OpCodes.Ret);                    // return from constructor
                typeBuilder.CreateType();
            }
            assemblyBuilder.Save(dllName + ".dll");
        }
    }
}
