using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using static PluginsManager.Const;

namespace PluginsManager
{
    public class CommandManager
    {
        public UIApplication UiApp { get; set; }
        public List<Type> AllTypes = new List<Type>();
        public List<Command> AllCommands = new List<Command>();
        public Dictionary<string, List<Command>> CommandsDictionary = new Dictionary<string, List<Command>>();
        public string FolderPath { get; set; }
        public ExternalEvent ExternalEvent { get; set; }
        public ConfigManager ConfigManager { get; set; }

        public CommandManager(UIApplication uiApp, string folderPath, ConfigManager commandConfigManager)
        {
            UiApp = uiApp;
            FolderPath = folderPath;
            ConfigManager = commandConfigManager;
            GetExternalCommandsFromAssembly(FolderPath);
            Handler eventHandler = new Handler(this);
            ExternalEvent externalEvent = ExternalEvent.Create(eventHandler);
            ExternalEvent = externalEvent;

        }

        public void Refresh(string folderPath)
        {
            AllTypes.Clear();
            AllCommands.Clear();
            CommandsDictionary.Clear();
            FolderPath = folderPath;
            GetExternalCommandsFromAssembly(FolderPath);
            Handler eventHandler = new Handler(this);
            ExternalEvent externalEvent = ExternalEvent.Create(eventHandler);
            ExternalEvent = externalEvent;
        }

        public void RunCommand(string commandName)
        {
            var commandType = AllTypes.FirstOrDefault(x => x.FullName == commandName);
            IExternalCommand commandInstance = (IExternalCommand)Activator.CreateInstance(commandType);
            ExternalCommandData commandData = Create(UiApp);
            string message = string.Empty;
            ElementSet elements = null;
            Result result = commandInstance.Execute(commandData, ref message, elements);
            if (result != Result.Succeeded)
            {
                TaskDialog.Show("Ошибка", message);
            }
        }

        public ExternalCommandData Create(UIApplication uiApplication)
        {
            // Находим тип ExternalCommandData
            Type externalCommandDataType = typeof(ExternalCommandData);

            // Находим внутренний конструктор
            ConstructorInfo constructor = externalCommandDataType
                .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault();

            if (constructor is null)
            {
                throw new InvalidOperationException("Не удалось найти конструктор ExternalCommandData.");
            }

            // Создаем экземпляр через рефлексию
            ExternalCommandData data = (ExternalCommandData)constructor.Invoke(null);

            // Устанавливаем свойство Application через рефлексию
            PropertyInfo applicationProperty = externalCommandDataType.GetProperty(
                Const.PropertyNames.Application,
                BindingFlags.Public | BindingFlags.Instance
            );

            if (applicationProperty != null && applicationProperty.CanWrite)
            {
                applicationProperty.SetValue(data, uiApplication);
            }
            else
            {
                throw new InvalidOperationException("Не удалось установить свойство Application.");
            }

            return data;
        }

private void GetExternalCommandsFromAssembly(string folderPath)
{
    try
    {
        var dllFiles = Directory.GetFiles(folderPath, "*.dll");
        var subFolders = Directory.GetDirectories(folderPath);
        foreach (var subFolder in subFolders)
        {
            dllFiles = dllFiles.Concat(Directory.GetFiles(subFolder, "*.dll")).ToArray();
        }

        foreach (var dllFile in dllFiles)
        {
            try
            {
                var assemblyBytes = File.ReadAllBytes(dllFile);
                var assembly = Assembly.Load(assemblyBytes);

                IEnumerable<Type> externalCommands = assembly.GetTypes()
                    .Where(type => typeof(IExternalCommand).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface);

                AllTypes.AddRange(externalCommands);

                foreach (var type in externalCommands)
                {
                    FillCommandsDictionaryAndList(type, assembly);
                }
            }
            catch
            {
            }

        }
        SortCommandsDictionary();
    }
    catch 
    {
    }
}

        private void FillCommandsDictionaryAndList(Type type, Assembly assembly)
        {
            var commandName = string.Empty;
            var tabName = string.Empty;
            var commandDescription = string.Empty;
            var commandImage = string.Empty;

            if (ConfigManager.CommamdConfigDictionary.ContainsKey(type.FullName))
            {
                commandName = ConfigManager.CommamdConfigDictionary[type.FullName][CmdConfigFile.XmlName[0]];
                tabName = ConfigManager.CommamdConfigDictionary[type.FullName][CmdConfigFile.XmlTab[0]];
                commandDescription = ConfigManager.CommamdConfigDictionary[type.FullName][CmdConfigFile.XmlDescription[0]];
                commandImage = ConfigManager.CommamdConfigDictionary[type.FullName][CmdConfigFile.XmlImage[0]];
            }
            else
            {
                commandName = type.GetProperty(Const.DllFields.Name, BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null)
                    ?.ToString();
                tabName = type.GetProperty(Const.DllFields.TabName, BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null)
                    ?.ToString();
                commandDescription = type.GetProperty(Const.DllFields.Description, BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null)
                    ?.ToString();
                commandImage = type.GetProperty(Const.DllFields.Image, BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null)
                    ?.ToString();
            }
            if (!string.IsNullOrEmpty(tabName))
            {
                Image image = Properties.Resources.imgPlaceholder;

                if (ConfigManager.CommamdConfigDictionary.ContainsKey(type.FullName))
                {
                    var path = Path.Combine(FolderPath, CmdConfigFile.ImageFolderName, commandImage);

                    if (File.Exists(path))
                    {
                        byte[] imageBytes = File.ReadAllBytes(path);
                        try
                        {
                            using (MemoryStream ms = new MemoryStream(imageBytes))
                            {
                                image = Image.FromStream(ms);
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(commandImage))
                    {
                        using (Stream stream = assembly.GetManifestResourceStream(commandImage))
                        {
                            if (stream != null)
                            {
                                image = Image.FromStream(stream);
                            }
                        }
                    }
                }
                var command = new Command(type.FullName, commandName, commandDescription, image);
                AllCommands.Add(command);

                if (tabName.Contains("|"))
                {
                    foreach (var tab in tabName.Split('|'))
                    {
                        if (!CommandsDictionary.ContainsKey(tab))
                        {
                            CommandsDictionary.Add(tab, new List<Command> { command });
                        }
                        else
                        {
                            CommandsDictionary[tab].Add(command);
                        }
                    }
                }
                else
                {
                    if (!CommandsDictionary.ContainsKey(tabName))
                    {
                        CommandsDictionary.Add(tabName, new List<Command> { command });
                    }
                    else
                    {
                        CommandsDictionary[tabName].Add(command);
                    }
                }
            }
        }
        private void SortCommandsDictionary()
        {
            if (CommandsDictionary != null)
            {
                CommandsDictionary = CommandsDictionary
                    .OrderBy(pair => pair.Key)
                    .ToDictionary(pair => pair.Key, pair => pair.Value);

                foreach (var key in CommandsDictionary.Keys.ToList())
                {
                    CommandsDictionary[key] = CommandsDictionary[key].OrderBy(val => val.CmdName).ToList();
                }
            }
        }
    }
}
