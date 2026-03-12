using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Protons.UI.Common;
using Protons.UI.Login.ViewModels;

namespace Protons.UI;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// Assunção: View e ViewModel estão no mesmo assembly; se View/ViewModel forem movidos para assemblies distintos, o locator deixa de resolver e volta a exibir "Not Found".
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var viewModelType = param.GetType();
        var viewName = viewModelType.FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        viewName = viewName.Replace(".ViewModels.", ".Views.", StringComparison.Ordinal);
        // Mesmo assembly: se View e ViewModel estiverem em assemblies diferentes, usar mapeamento explícito ou resolver pelo assembly da View.
        var type = viewModelType.Assembly.GetType(viewName);

#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[ViewLocator] ViewModel: {viewModelType.FullName}");
        System.Diagnostics.Debug.WriteLine($"[ViewLocator] View Name: {viewName}");
        System.Diagnostics.Debug.WriteLine($"[ViewLocator] Type Found: {type?.FullName ?? "NULL"}");
#endif

        OpsLogger.WriteInfo($"ViewLocator: resolving {viewModelType.Name} -> {viewName.Split('.').Last()} (found: {type != null})");

        if (type != null)
        {
            var sw = Stopwatch.StartNew();
            OpsLogger.WriteInfo($"viewlocator_start: type={type.Name}");
            try
            {
                var view = (Control)Activator.CreateInstance(type)!;
                sw.Stop();
                OpsLogger.WriteInfo($"viewlocator_elapsed: type={type.Name} ms={sw.ElapsedMilliseconds}");
                OpsLogger.WriteInfo($"ViewLocator: successfully created {type.Name}");
                return view;
            }
            catch (Exception ex)
            {
                sw.Stop();
                // Capturar a exceção real (InnerException geralmente contém o erro verdadeiro)
                var realException = ex.InnerException ?? ex;
                OpsLogger.WriteInfo($"viewlocator_elapsed: type={type.Name} ms={sw.ElapsedMilliseconds} error={realException.GetType().Name}");
                var errorMsg = $"ViewLocator: error creating {type.Name}: {realException.GetType().Name}: {realException.Message}";
                OpsLogger.WriteError(errorMsg, realException);
                Debug.WriteLine($"[ViewLocator ERROR] {errorMsg}");
                Debug.WriteLine(realException.StackTrace);
                return new TextBlock { Text = $"Erro: {realException.Message}" };
            }
        }

        OpsLogger.WriteWarning($"ViewLocator: view not found for {viewName}");
        return new TextBlock { Text = "Tela não encontrada: " + viewName.Split('.').Last() };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
