using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace OpenWigi.Manager.Services;

/// <summary>Janelas de abrir e salvar do Windows, chamadas da UI em Blazor. Rodam na thread do WPF, presas à janela do manager.</summary>
public static class FilePicker
{
    /// <summary>Pergunta onde salvar. null = a pessoa cancelou.</summary>
    public static string? Save(string title, string filter, string suggestedName, string? folder = null) =>
        Show(new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = suggestedName,
            InitialDirectory = folder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = true,
            AddExtension = true,
        });

    /// <summary>Pergunta que arquivo abrir. null = a pessoa cancelou.</summary>
    public static string? Open(string title, string filter, string? folder = null) =>
        Show(new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            InitialDirectory = folder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            CheckFileExists = true,
        });

    private static string? Show(FileDialog dialog) =>
        Application.Current.Dispatcher.Invoke(() =>
        {
            var owner = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            var picked = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            return picked == true ? dialog.FileName : null;
        });
}
