﻿namespace Example2
{
    using UnityEngine;

    //记录数据给服务器使用
    public class MonsterPoint : MonoBehaviour
    {
        [Header("对应SceneManager组件的monsters字段索引")]
        public MonsterData[] monsters;
    }
}