﻿#if UNITY_EDITOR
using System.IO;
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;

public class Fast2BuildTools1 : EditorWindow
{
    private string bindTypeName = "Net.Binding.BindingEntry";
    private string methodName = "GetBindTypes";
    private string savePath;
    private string bindTypeName1;
    private string methodName1;
    private bool serField = true;
    private bool serProperty = true;

    [MenuItem("GameDesigner/Network/Fast2BuildTool-1")]
    static void ShowWindow()
    {
        var window = GetWindow<Fast2BuildTools1>("快速序列化2生成工具");
        window.position = new Rect(window.position.position, new Vector2(400,200));
        window.Show();
    }

    private void OnEnable()
    {
        var path = Application.dataPath.Replace("Assets", "") + "data1.txt";
        if (File.Exists(path))
        {
            var jsonStr = File.ReadAllText(path);
            var data = Newtonsoft_X.Json.JsonConvert.DeserializeObject<Data>(jsonStr);
            bindTypeName = data.typeName;
            methodName = data.methodName;
            savePath = data.savepath;
        }
    }

    private void OnGUI()
    {
        bindTypeName = EditorGUILayout.TextField("入口类型:", bindTypeName);
        methodName = EditorGUILayout.TextField("入口方法:", methodName);
        if (bindTypeName != bindTypeName1 | methodName != methodName1)
        {
            bindTypeName1 = bindTypeName;
            methodName1 = methodName;
            Save();
        }
        serField = EditorGUILayout.Toggle("序列化字段:", serField);
        serProperty = EditorGUILayout.Toggle("序列化属性:", serProperty);
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("保存路径:", savePath);
        if (GUILayout.Button("选择路径", GUILayout.Width(100)))
        {
            savePath = EditorUtility.OpenFolderPanel("保存路径", "", "");
            Save();
        }
        GUILayout.EndHorizontal();
        if (GUILayout.Button("生成序列化代码", GUILayout.Height(30)))
        {
            if (string.IsNullOrEmpty(savePath)) 
            {
                EditorUtility.DisplayDialog("提示", "请选择生成脚本路径!", "确定");
                return;
            }
            var assembly = Assembly.GetAssembly(typeof(Net.Binding.BindingEntry));
            Debug.Log(assembly);
            var bindType = assembly.GetType(bindTypeName);
            if (bindType == null)
                throw new Exception("获取类型失败!");
            var method = bindType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
            IList<Type> list = (IList<Type>)method.Invoke(null, null);
            foreach (var type in list)
            {
                Fast2BuildMethod.Build(type, true, savePath, serField, serProperty, new List<string>());
                Fast2BuildMethod.BuildArray(type, true, savePath);
                Fast2BuildMethod.BuildGeneric(type, true, savePath);
            }
            Debug.Log("生成完成.");
            AssetDatabase.Refresh();
        }
        EditorGUILayout.HelpBox("指定主入口类型和调用入口方法，然后选择生成代码文件夹路径，最后点击生成。绑定入口案例:请看Net.Binding.BindingEntry类的GetBindTypes方法", MessageType.Info);
    }

    void Save()
    {
        Data data = new Data() { typeName = bindTypeName, methodName = methodName, savepath = savePath };
        var jsonstr = Newtonsoft_X.Json.JsonConvert.SerializeObject(data);
        var path = Application.dataPath.Replace("Assets", "") + "data1.txt";
        File.WriteAllText(path, jsonstr);
    }

    internal class Data
    {
        public string typeName;
        public string methodName;
        public string savepath;
    }
}

public class Fast2BuildTools2 : EditorWindow
{
    private List<FoldoutData> typeNames = new List<FoldoutData>();
    private bool selectType;
    private string search = "", search1 = "";
    private DateTime searchTime;
    private TypeData[] types;
    private Vector2 scrollPosition;
    private Vector2 scrollPosition1;
    private string savePath, savePath1;
    private bool serField = true;
    private bool serProperty = true;

    [MenuItem("GameDesigner/Network/Fast2BuildTool-2")]
    static void ShowWindow()
    {
        var window = GetWindow<Fast2BuildTools2>("快速序列化2生成工具");
        //window.position = new Rect(window.position.position, new Vector2(400, 200));
        window.Show();
    }

    private void OnEnable()
    {
        List<TypeData> types1 = new List<TypeData>();
        var types2 = typeof(MVC.Control.GameInit).Assembly.GetTypes().Where(t => !t.IsAbstract & !t.IsInterface & !t.IsGenericType & !t.IsGenericType & !t.IsGenericTypeDefinition).ToArray();
        var types3 = typeof(Vector2).Assembly.GetTypes().Where(t => !t.IsAbstract & !t.IsInterface & !t.IsGenericType & !t.IsGenericType & !t.IsGenericTypeDefinition).ToArray();
        var typeslist = new List<Type>(types2);
        typeslist.AddRange(types3);
        foreach (var obj in typeslist)
        {
            var str = obj.FullName;
            types1.Add(new TypeData() { name = str, type = obj });
        }
        types = types1.ToArray();
        LoadData();
    }

    private void OnGUI()
    {
        search = EditorGUILayout.TextField("搜索绑定类型", search);
        EditorGUILayout.LabelField("绑定类型列表:");
        if (typeNames.Count != 0)
        {
            scrollPosition1 = GUILayout.BeginScrollView(scrollPosition1, false, true, GUILayout.MaxHeight(position.height / 2));
            EditorGUI.BeginChangeCheck();
            foreach (var type1 in typeNames)
            {
                var rect = EditorGUILayout.GetControlRect();
                type1.foldout = EditorGUI.Foldout(new Rect(rect.position, rect.size - new Vector2(50, 0)), type1.foldout, type1.name, true);
                if (type1.foldout)
                {
                    EditorGUI.indentLevel = 1;
                    for (int i = 0; i < type1.fields.Count; i++)
                    {
                        type1.fields[i].serialize = EditorGUILayout.Toggle(type1.fields[i].name, type1.fields[i].serialize);
                    }
                    EditorGUI.indentLevel = 0;
                }
                if (GUI.Button(new Rect(rect.position + new Vector2(position.width - 50, 0), new Vector2(20, rect.height)), "x"))
                {
                    typeNames.Remove(type1);
                    SaveData();
                    return;
                }
                if (rect.Contains(Event.current.mousePosition) & Event.current.button == 1)
                {
                    GenericMenu menu = new GenericMenu();
                    menu.AddItem(new GUIContent("全部勾上"), false, ()=>
                    {
                        type1.fields.ForEach(item => item.serialize = true);
                    }); 
                    menu.AddItem(new GUIContent("全部取消"), false, () =>
                    {
                        type1.fields.ForEach(item => item.serialize = false);
                    });
                    menu.AddItem(new GUIContent("更新字段"), false, () =>
                    {
                        UpdateField(type1);
                        SaveData();
                    });
                    menu.AddItem(new GUIContent("全部字段更新"), false, () =>
                    {
                        UpdateFields();
                        SaveData();
                        Debug.Log("全部字段已更新完成!");
                    });
                    menu.AddItem(new GUIContent("移除"), false, () =>
                    {
                        typeNames.Remove(type1);
                        SaveData();
                    });
                    menu.ShowAsContext();
                }
            }
            if (EditorGUI.EndChangeCheck())
                SaveData();
            GUILayout.EndScrollView();
        }
        if (search != search1)
        {
            selectType = false;
            search1 = search;
            searchTime = DateTime.Now.AddMilliseconds(20);
        }
        if (DateTime.Now > searchTime & !selectType & search.Length > 0)
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.MaxHeight(position.height / 2));
            foreach (var type1 in types)
            {
                if (!type1.name.ToLower().Contains(search.ToLower()))
                    continue;
                if (GUILayout.Button(type1.name))
                {
                    if (typeNames.Find(item => item.name == type1.name) == null)
                    {
                        var fields = type1.type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                        var properties = type1.type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                        var fields1 = new List<FieldData>();
                        foreach (var item in fields)
                        {
                            if (item.GetCustomAttribute<Net.Serialize.NonSerialized>() != null)
                                continue;
                            fields1.Add(new FieldData() { name = item.Name, serialize = true });
                        }
                        foreach (var item in properties)
                        {
                            if (!item.CanRead | !item.CanWrite)
                                continue;
                            if (item.GetIndexParameters().Length > 0)
                                continue;
                            if (item.GetCustomAttribute<Net.Serialize.NonSerialized>() != null)
                                continue;
                            fields1.Add(new FieldData() { name = item.Name, serialize = true });
                        }
                        typeNames.Add(new FoldoutData() { name = type1.name, fields = fields1, foldout = false });
                    }
                    SaveData();
                    return;
                }
            }
            GUILayout.EndScrollView();
        }
        serField = EditorGUILayout.Toggle("序列化字段:", serField);
        serProperty = EditorGUILayout.Toggle("序列化属性:", serProperty);
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("保存路径:", savePath);
        if (GUILayout.Button("选择路径", GUILayout.Width(100)))
        {
            savePath = EditorUtility.OpenFolderPanel("保存路径", "", "");
            SaveData();
        }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("保存路径1:", savePath1);
        if (GUILayout.Button("选择路径1", GUILayout.Width(100)))
        {
            savePath1 = EditorUtility.OpenFolderPanel("保存路径", "", "");
            SaveData();
        }
        GUILayout.EndHorizontal();
        if (GUILayout.Button("生成绑定代码", GUILayout.Height(30)))
        {
            if (string.IsNullOrEmpty(savePath))
            {
                EditorUtility.DisplayDialog("提示", "请选择生成脚本路径!", "确定");
                return;
            }
            foreach (var type1 in typeNames)
            {
                Type type = Net.Serialize.NetConvertOld.GetType(type1.name);
                Fast2BuildMethod.Build(type, true, savePath, serField, serProperty, type1.fields.ConvertAll((item)=> !item.serialize ? item.name : ""));
                Fast2BuildMethod.BuildArray(type, true, savePath);
                Fast2BuildMethod.BuildGeneric(type, true, savePath);
                if (!string.IsNullOrEmpty(savePath1)) 
                {
                    Fast2BuildMethod.Build(type, true, savePath1, serField, serProperty, type1.fields.ConvertAll((item) => !item.serialize ? item.name : ""));
                    Fast2BuildMethod.BuildArray(type, true, savePath1);
                    Fast2BuildMethod.BuildGeneric(type, true, savePath1);
                }
            }
            Debug.Log("生成完成.");
            AssetDatabase.Refresh();
        }
        EditorGUILayout.HelpBox("指定主入口类型和调用入口方法，然后选择生成代码文件夹路径，最后点击生成。绑定入口案例:请看Net.Binding.BindingEntry类的GetBindTypes方法", MessageType.Info);
    }

    private void UpdateFields() 
    {
        foreach (var fd in typeNames) 
        {
            UpdateField(fd);
        }
        SaveData();
    }

    private void UpdateField(FoldoutData fd)
    {
        Type type = null;
        foreach (var type2 in types)
        {
            if (fd.name == type2.name)
            {
                type = type2.type;
                break;
            }
        }
        if (type == null)
        {
            Debug.Log(fd.name + "类型为空!");
            return;
        }
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var fields1 = new List<FieldData>();
        foreach (var item in fields)
        {
            if (item.GetCustomAttribute<Net.Serialize.NonSerialized>() != null)
                continue;
            fields1.Add(new FieldData() { name = item.Name, serialize = true });
        }
        foreach (var item in properties)
        {
            if (!item.CanRead | !item.CanWrite)
                continue;
            if (item.GetIndexParameters().Length > 0)
                continue;
            if (item.GetCustomAttribute<Net.Serialize.NonSerialized>() != null)
                continue;
            fields1.Add(new FieldData() { name = item.Name, serialize = true });
        }
        foreach (var item in fields1)
        {
            if (fd.fields.Find(item1 => item1.name == item.name, out var fd1))
            {
                item.serialize = fd1.serialize;
            }
        }
        fd.fields = fields1;
    }

    void LoadData() 
    {
        var path = Application.dataPath.Replace("Assets", "") + "data2.txt";
        if (File.Exists(path))
        {
            var jsonStr = File.ReadAllText(path);
            var data = Newtonsoft_X.Json.JsonConvert.DeserializeObject<Data>(jsonStr);
            typeNames = data.typeNames;
            savePath = data.savepath; 
            savePath1 = data.savepath1;
        }
    }

    void SaveData()
    {
        Data data = new Data() { 
            typeNames = typeNames,
            savepath = savePath,
            savepath1 = savePath1,
        };
        var jsonstr = Newtonsoft_X.Json.JsonConvert.SerializeObject(data);
        var path = Application.dataPath.Replace("Assets", "") + "data2.txt";
        File.WriteAllText(path, jsonstr);
    }

    internal class FoldoutData 
    {
        public string name;
        public bool foldout;
        public List<FieldData> fields = new List<FieldData>();
    }

    internal class FieldData 
    {
        public string name;
        public bool serialize;
    }

    internal class TypeData 
    {
        public string name;
        public Type type;
    }

    internal class Data
    {
        public string savepath, savepath1;
        public List<FoldoutData> typeNames;
    }
}
#endif