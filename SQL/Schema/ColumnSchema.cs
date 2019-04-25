﻿using CYQ.Data.Cache;
using CYQ.Data.Orm;
using CYQ.Data.Table;
using CYQ.Data.Tool;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using System.IO;
using System.Reflection;
using System.Text;

namespace CYQ.Data.SQL
{
    internal partial class ColumnSchema
    {
        /// <summary>
        /// 全局缓存实体类的表结构（仅会对Type读取的结构）
        /// </summary>
        private static MDictionary<string, MDataColumn> _ColumnCache = new MDictionary<string, MDataColumn>(StringComparer.OrdinalIgnoreCase);
        public static MDataColumn GetColumns(Type typeInfo)
        {
            string key = "ColumnCache_" + typeInfo.FullName;
            if (_ColumnCache.ContainsKey(key))
            {
                return _ColumnCache[key].Clone();
            }
            else
            {
                #region 获取列结构
                MDataColumn mdc = new MDataColumn();
                mdc.TableName = typeInfo.Name;
                switch (StaticTool.GetSystemType(ref typeInfo))
                {
                    case SysType.Base:
                    case SysType.Enum:
                        mdc.Add(typeInfo.Name, DataType.GetSqlType(typeInfo), false);
                        return mdc;
                    case SysType.Generic:
                    case SysType.Collection:
                        Type[] argTypes;
                        Tool.StaticTool.GetArgumentLength(ref typeInfo, out argTypes);
                        foreach (Type type in argTypes)
                        {
                            mdc.Add(type.Name, DataType.GetSqlType(type), false);
                        }
                        argTypes = null;
                        return mdc;

                }

                List<PropertyInfo> pis = StaticTool.GetPropertyInfo(typeInfo);
                if (pis.Count > 0)
                {
                    for (int i = 0; i < pis.Count; i++)
                    {
                        SetStruct(mdc, pis[i], null, i, pis.Count);
                    }
                }
                else
                {
                    List<FieldInfo> fis = StaticTool.GetFieldInfo(typeInfo);
                    if (fis.Count > 0)
                    {
                        for (int i = 0; i < fis.Count; i++)
                        {
                            SetStruct(mdc, null, fis[i], i, fis.Count);
                        }
                    }
                }
                object[] tableAttr = typeInfo.GetCustomAttributes(typeof(DescriptionAttribute), false);//看是否设置了表特性，获取表名和表描述
                if (tableAttr != null && tableAttr.Length == 1)
                {
                    DescriptionAttribute attr = tableAttr[0] as DescriptionAttribute;
                    if (attr != null && !string.IsNullOrEmpty(attr.Description))
                    {
                        mdc.Description = attr.Description;
                    }
                }
                pis = null;
                #endregion

                if (!_ColumnCache.ContainsKey(key))
                {
                    _ColumnCache.Set(key, mdc.Clone());
                }

                return mdc;
            }

        }
        private static void SetStruct(MDataColumn mdc, PropertyInfo pi, FieldInfo fi, int i, int count)
        {
            Type type = pi != null ? pi.PropertyType : fi.FieldType;
            string name = pi != null ? pi.Name : fi.Name;
            SqlDbType sqlType = SQL.DataType.GetSqlType(type);
            mdc.Add(name, sqlType);
            MCellStruct column = mdc[i];
            LengthAttribute la = GetAttr<LengthAttribute>(pi, fi);//获取长度设置
            if (la != null)
            {
                column.MaxSize = la.MaxSize;
                column.Scale = la.Scale;
            }
            if (column.MaxSize <= 0)
            {
                column.MaxSize = DataType.GetMaxSize(sqlType);
            }
            KeyAttribute ka = GetAttr<KeyAttribute>(pi, fi);//获取关键字判断
            if (ka != null)
            {
                column.IsPrimaryKey = ka.IsPrimaryKey;
                column.IsAutoIncrement = ka.IsAutoIncrement;
                column.IsCanNull = ka.IsCanNull;
            }
            else if (i == 0)
            {
                column.IsPrimaryKey = true;
                column.IsCanNull = false;
                if (column.ColumnName.ToLower().Contains("id") && (column.SqlType == System.Data.SqlDbType.Int || column.SqlType == SqlDbType.BigInt))
                {
                    column.IsAutoIncrement = true;
                }
            }
            DefaultValueAttribute dva = GetAttr<DefaultValueAttribute>(pi, fi);
            if (dva != null && dva.DefaultValue != null)
            {
                if (column.SqlType == SqlDbType.Bit)
                {
                    column.DefaultValue = (dva.DefaultValue.ToString() == "True" || dva.DefaultValue.ToString() == "1") ? 1 : 0;
                }
                else
                {
                    column.DefaultValue = dva.DefaultValue;
                }
            }
            else if (i > count - 3 && sqlType == SqlDbType.DateTime && name.EndsWith("Time"))
            {
                column.DefaultValue = SqlValue.GetDate;
            }
            DescriptionAttribute da = GetAttr<DescriptionAttribute>(pi, fi);//看是否有字段描述属性。
            if (da != null)
            {
                column.Description = da.Description;
            }
        }
        private static T GetAttr<T>(PropertyInfo pi, FieldInfo fi)
        {
            Type type = typeof(T);
            object[] attr = pi != null ? pi.GetCustomAttributes(type, false) : fi.GetCustomAttributes(type, false);//看是否设置了特性

            if (attr != null && attr.Length == 1)
            {
                return (T)attr[0];
            }
            return default(T);
        }

        //private static KeyAttribute GetKeyAttr(PropertyInfo pi)
        //{
        //    object[] attr = pi.GetCustomAttributes(typeof(KeyAttribute), false);//看是否设置了表特性，获取表名和表描述
        //    if (attr != null && attr.Length == 1)
        //    {
        //        return attr[0] as KeyAttribute;
        //    }
        //    return null;
        //}
        //private static LengthAttribute GetLengthAttr(PropertyInfo pi)
        //{
        //    object[] attr = pi.GetCustomAttributes(typeof(LengthAttribute), false);//看是否设置了表特性，获取表名和表描述
        //    if (attr != null && attr.Length == 1)
        //    {
        //        return attr[0] as LengthAttribute;
        //    }
        //    return null;
        //}
        //private static DefaultValueAttribute GetDefaultValueAttr(PropertyInfo pi)
        //{
        //    object[] attr = pi.GetCustomAttributes(typeof(DefaultValueAttribute), false);//看是否设置了表特性，获取表名和表描述
        //    if (attr != null && attr.Length == 1)
        //    {
        //        return attr[0] as DefaultValueAttribute;
        //    }
        //    return null;
        //}
        private static DescriptionAttribute GetDescriptionAttr(PropertyInfo pi)
        {
            object[] attr = pi.GetCustomAttributes(typeof(DescriptionAttribute), false);//看是否设置了表特性，获取表名和表描述
            if (attr != null && attr.Length == 1)
            {
                return attr[0] as DescriptionAttribute;
            }
            return null;
        }
        public static MDataColumn GetColumns(string tableName, string conn)
        {

            string key = GetSchemaKey(tableName, conn);
            if (CacheManage.LocalInstance.Contains(key))//缓存里获取
            {
                return CacheManage.LocalInstance.Get<MDataColumn>(key).Clone();
            }
            string fixName;
            conn = CrossDB.GetConn(tableName, out fixName, conn);
            tableName = fixName;

            MDataColumn mdcs = null;
            using (DalBase dbHelper = DalCreate.CreateDal(conn))
            {
                DalType dalType = dbHelper.DataBaseType;

                #region 文本数据库处理。
                if (dalType == DalType.Txt || dalType == DalType.Xml)
                {
                    if (!tableName.Contains(" "))// || tableName.IndexOfAny(Path.GetInvalidPathChars()) == -1
                    {
                        tableName = SqlFormat.NotKeyword(tableName);//处理database..tableName;
                        tableName = Path.GetFileNameWithoutExtension(tableName);//视图表，带“.“的，会出问题
                        string fileName = dbHelper.Con.DataSource + tableName + (dalType == DalType.Txt ? ".txt" : ".xml");
                        mdcs = MDataColumn.CreateFrom(fileName);
                        mdcs.dalType = dalType;
                        return mdcs;
                    }
                    return GetTxtDBViewColumns(tableName);//处理视图
                }
                #endregion

                mdcs = new MDataColumn();
                mdcs.TableName = tableName;
                mdcs.dalType = dalType;

                tableName = SqlFormat.Keyword(tableName, dbHelper.DataBaseType);//加上关键字：引号
                //如果table和helper不在同一个库
                DalBase helper = dbHelper.ResetDalBase(tableName);

                helper.IsRecordDebugInfo = false;//内部系统，不记录SQL表结构语句。
                try
                {
                    bool isView = tableName.Contains(" ");//是否视图。
                    if (!isView)
                    {
                        isView = TableSchema.Exists(tableName, "V", conn);
                    }
                    if (!isView)
                    {
                        mdcs.Description = TableSchema.GetTableDescription(conn, mdcs.TableName);
                    }
                    MCellStruct mStruct = null;
                    SqlDbType sqlType = SqlDbType.NVarChar;
                    if (isView)
                    {
                        string sqlText = SqlFormat.BuildSqlWithWhereOneEqualsTow(tableName);// string.Format("select * from {0} where 1=2", tableName);
                        mdcs = GetViewColumns(sqlText, ref helper);
                    }
                    else
                    {
                        mdcs.AddRelateionTableName(SqlFormat.NotKeyword(tableName));
                        switch (dalType)
                        {
                            case DalType.MsSql:
                            case DalType.Oracle:
                            case DalType.MySql:
                            case DalType.Sybase:
                            case DalType.PostgreSQL:
                                #region Sql
                                string sql = string.Empty;
                                if (dalType == DalType.MsSql)
                                {
                                    #region Mssql
                                    string dbName = null;
                                    if (!helper.Version.StartsWith("08"))
                                    {
                                        //先获取同义词，检测是否跨库
                                        string realTableName = Convert.ToString(helper.ExeScalar(string.Format(MSSQL_SynonymsName, SqlFormat.NotKeyword(tableName)), false));
                                        if (!string.IsNullOrEmpty(realTableName))
                                        {
                                            string[] items = realTableName.Split('.');
                                            tableName = realTableName;
                                            if (items.Length > 0)//跨库了
                                            {
                                                dbName = realTableName.Split('.')[0];
                                            }
                                        }
                                    }

                                    sql = GetMSSQLColumns(helper.Version.StartsWith("08"), dbName ?? helper.DataBase);
                                    #endregion
                                }
                                else if (dalType == DalType.MySql)
                                {
                                    sql = GetMySqlColumns(helper.DataBase);
                                }
                                else if (dalType == DalType.Oracle)
                                {
                                    tableName = tableName.ToUpper();//Oracle转大写。
                                    //先获取同义词，不检测是否跨库
                                    string realTableName = Convert.ToString(helper.ExeScalar(string.Format(Oracle_SynonymsName, SqlFormat.NotKeyword(tableName)), false));
                                    if (!string.IsNullOrEmpty(realTableName))
                                    {
                                        tableName = realTableName;
                                    }

                                    sql = GetOracleColumns();
                                }
                                else if (dalType == DalType.Sybase)
                                {
                                    tableName = SqlFormat.NotKeyword(tableName);
                                    sql = GetSybaseColumns();
                                }
                                else if (dalType == DalType.PostgreSQL)
                                {
                                    sql = GetPostgreColumns();
                                }
                                helper.AddParameters("TableName", SqlFormat.NotKeyword(tableName), DbType.String, 150, ParameterDirection.Input);
                                DbDataReader sdr = helper.ExeDataReader(sql, false);
                                if (sdr != null)
                                {
                                    long maxLength;
                                    bool isAutoIncrement = false;
                                    short scale = 0;
                                    string sqlTypeName = string.Empty;
                                    while (sdr.Read())
                                    {
                                        short.TryParse(Convert.ToString(sdr["Scale"]), out scale);
                                        if (!long.TryParse(Convert.ToString(sdr["MaxSize"]), out maxLength))//mysql的长度可能大于int.MaxValue
                                        {
                                            maxLength = -1;
                                        }
                                        else if (maxLength > int.MaxValue)
                                        {
                                            maxLength = int.MaxValue;
                                        }
                                        sqlTypeName = Convert.ToString(sdr["SqlType"]);
                                        sqlType = DataType.GetSqlType(sqlTypeName);
                                        isAutoIncrement = Convert.ToBoolean(sdr["IsAutoIncrement"]);
                                        mStruct = new MCellStruct(mdcs.dalType);
                                        mStruct.ColumnName = Convert.ToString(sdr["ColumnName"]).Trim();
                                        mStruct.OldName = mStruct.ColumnName;
                                        mStruct.SqlType = sqlType;
                                        mStruct.IsAutoIncrement = isAutoIncrement;
                                        mStruct.IsCanNull = Convert.ToBoolean(sdr["IsNullable"]);
                                        mStruct.MaxSize = (int)maxLength;
                                        mStruct.Scale = scale;
                                        mStruct.Description = Convert.ToString(sdr["Description"]);
                                        mStruct.DefaultValue = SqlFormat.FormatDefaultValue(dalType, sdr["DefaultValue"], 0, sqlType);
                                        mStruct.IsPrimaryKey = Convert.ToString(sdr["IsPrimaryKey"]) == "1";
                                        switch (dalType)
                                        {
                                            case DalType.MsSql:
                                            case DalType.MySql:
                                            case DalType.Oracle:
                                                mStruct.IsUniqueKey = Convert.ToString(sdr["IsUniqueKey"]) == "1";
                                                mStruct.IsForeignKey = Convert.ToString(sdr["IsForeignKey"]) == "1";
                                                mStruct.FKTableName = Convert.ToString(sdr["FKTableName"]);
                                                break;
                                        }

                                        mStruct.SqlTypeName = sqlTypeName;
                                        mStruct.TableName = SqlFormat.NotKeyword(tableName);
                                        mdcs.Add(mStruct);
                                    }
                                    sdr.Close();
                                    if (dalType == DalType.Oracle && mdcs.Count > 0)//默认没有自增概念，只能根据情况判断。
                                    {
                                        MCellStruct firstColumn = mdcs[0];
                                        if (firstColumn.IsPrimaryKey && firstColumn.ColumnName.ToLower().Contains("id") && firstColumn.Scale == 0 && DataType.GetGroup(firstColumn.SqlType) == 1 && mdcs.JointPrimary.Count == 1)
                                        {
                                            firstColumn.IsAutoIncrement = true;
                                        }
                                    }
                                }
                                #endregion
                                break;
                            case DalType.SQLite:
                                #region SQlite
                                if (helper.Con.State != ConnectionState.Open)
                                {
                                    helper.Con.Open();
                                }
                                DataTable sqliteDt = helper.Con.GetSchema("Columns", new string[] { null, null, SqlFormat.NotKeyword(tableName) });
                                if (!helper.IsOpenTrans)
                                {
                                    helper.Con.Close();
                                }
                                int size;
                                short sizeScale;
                                string dataTypeName = string.Empty;

                                foreach (DataRow row in sqliteDt.Rows)
                                {
                                    object len = row["NUMERIC_PRECISION"];
                                    if (len == null || len == DBNull.Value)
                                    {
                                        len = row["CHARACTER_MAXIMUM_LENGTH"];
                                    }
                                    short.TryParse(Convert.ToString(row["NUMERIC_SCALE"]), out sizeScale);
                                    if (!int.TryParse(Convert.ToString(len), out size))//mysql的长度可能大于int.MaxValue
                                    {
                                        size = -1;
                                    }
                                    dataTypeName = Convert.ToString(row["DATA_TYPE"]);
                                    if (dataTypeName == "text" && size > 0)
                                    {
                                        sqlType = DataType.GetSqlType("varchar");
                                    }
                                    else
                                    {
                                        sqlType = DataType.GetSqlType(dataTypeName);
                                    }
                                    //COLUMN_NAME,DATA_TYPE,PRIMARY_KEY,IS_NULLABLE,CHARACTER_MAXIMUM_LENGTH AUTOINCREMENT

                                    mStruct = new MCellStruct(row["COLUMN_NAME"].ToString(), sqlType, Convert.ToBoolean(row["AUTOINCREMENT"]), Convert.ToBoolean(row["IS_NULLABLE"]), size);
                                    mStruct.Scale = sizeScale;
                                    mStruct.Description = Convert.ToString(row["DESCRIPTION"]);
                                    mStruct.DefaultValue = SqlFormat.FormatDefaultValue(dalType, row["COLUMN_DEFAULT"], 0, sqlType);//"COLUMN_DEFAULT"
                                    mStruct.IsPrimaryKey = Convert.ToBoolean(row["PRIMARY_KEY"]);
                                    mStruct.SqlTypeName = dataTypeName;
                                    mStruct.TableName = SqlFormat.NotKeyword(tableName);
                                    mdcs.Add(mStruct);
                                }
                                #endregion
                                break;
                            case DalType.Access:
                                #region Access
                                DataTable keyDt, valueDt;
                                string sqlText = SqlFormat.BuildSqlWithWhereOneEqualsTow(tableName);// string.Format("select * from {0} where 1=2", tableName);
                                OleDbConnection con = new OleDbConnection(helper.Con.ConnectionString);
                                OleDbCommand com = new OleDbCommand(sqlText, con);
                                con.Open();
                                keyDt = com.ExecuteReader(CommandBehavior.KeyInfo).GetSchemaTable();
                                valueDt = con.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object[] { null, null, SqlFormat.NotKeyword(tableName) });
                                con.Close();
                                con.Dispose();

                                if (keyDt != null && valueDt != null)
                                {
                                    string columnName = string.Empty, sqlTypeName = string.Empty;
                                    bool isKey = false, isCanNull = true, isAutoIncrement = false;
                                    int maxSize = -1;
                                    short maxSizeScale = 0;
                                    SqlDbType sqlDbType;
                                    foreach (DataRow row in keyDt.Rows)
                                    {
                                        columnName = row["ColumnName"].ToString();
                                        isKey = Convert.ToBoolean(row["IsKey"]);//IsKey
                                        isCanNull = Convert.ToBoolean(row["AllowDBNull"]);//AllowDBNull
                                        isAutoIncrement = Convert.ToBoolean(row["IsAutoIncrement"]);
                                        sqlTypeName = Convert.ToString(row["DataType"]);
                                        sqlDbType = DataType.GetSqlType(sqlTypeName);
                                        short.TryParse(Convert.ToString(row["NumericScale"]), out maxSizeScale);
                                        if (Convert.ToInt32(row["NumericPrecision"]) > 0)//NumericPrecision
                                        {
                                            maxSize = Convert.ToInt32(row["NumericPrecision"]);
                                        }
                                        else
                                        {
                                            long len = Convert.ToInt64(row["ColumnSize"]);
                                            if (len > int.MaxValue)
                                            {
                                                maxSize = int.MaxValue;
                                            }
                                            else
                                            {
                                                maxSize = (int)len;
                                            }
                                        }
                                        mStruct = new MCellStruct(columnName, sqlDbType, isAutoIncrement, isCanNull, maxSize);
                                        mStruct.Scale = maxSizeScale;
                                        mStruct.IsPrimaryKey = isKey;
                                        mStruct.SqlTypeName = sqlTypeName;
                                        mStruct.TableName = SqlFormat.NotKeyword(tableName);
                                        foreach (DataRow item in valueDt.Rows)
                                        {
                                            if (columnName == item[3].ToString())//COLUMN_NAME
                                            {
                                                if (item[8].ToString() != "")
                                                {
                                                    mStruct.DefaultValue = SqlFormat.FormatDefaultValue(dalType, item[8], 0, sqlDbType);//"COLUMN_DEFAULT"
                                                }
                                                break;
                                            }
                                        }
                                        mdcs.Add(mStruct);
                                    }

                                }

                                #endregion
                                break;
                        }
                    }
                }
                catch (Exception err)
                {
                    helper.DebugInfo.Append(err.Message);
                }
            }
            if (mdcs.Count > 0)
            {
                //移除被标志的列：
                string[] fields = AppConfig.DB.HiddenFields.Split(',');
                foreach (string item in fields)
                {
                    string field = item.Trim();
                    if (!string.IsNullOrEmpty(field) & mdcs.Contains(field))
                    {
                        mdcs.Remove(field);
                    }
                }
            }
            if (!CacheManage.LocalInstance.Contains(key) && mdcs.Count > 0)//拿不到表结构时不缓存。
            {
                CacheManage.LocalInstance.Set(key, mdcs.Clone());
            }
            return mdcs;
        }
        /// <summary>
        /// DbDataReader的GetSchema拿到的DataType、Size、Scale很不靠谱
        /// </summary>
        internal static void FixTableSchemaType(DbDataReader sdr, DataTable tableSchema)
        {
            if (sdr != null && tableSchema != null)
            {
                tableSchema.Columns.Add("DataTypeString");
                for (int i = 0; i < sdr.FieldCount; i++)
                {
                    tableSchema.Rows[i]["DataTypeString"] = sdr.GetDataTypeName(i);
                }
            }
        }
        internal static MDataColumn GetColumns(DataTable tableSchema)
        {
            MDataColumn mdcs = new MDataColumn();
            if (tableSchema != null && tableSchema.Rows.Count > 0)
            {
                mdcs.TableName = tableSchema.TableName;
                mdcs.isViewOwner = true;
                string columnName = string.Empty, sqlTypeName = string.Empty, tableName = string.Empty;
                bool isKey = false, isCanNull = true, isAutoIncrement = false;
                int maxSize = -1;
                short maxSizeScale = 0;
                SqlDbType sqlDbType;
                string dataTypeName = "DataTypeString";
                if (!tableSchema.Columns.Contains(dataTypeName))
                {
                    dataTypeName = "DataType";
                    if (!tableSchema.Columns.Contains(dataTypeName))
                    {
                        dataTypeName = "DataTypeName";
                    }
                }
                bool isHasAutoIncrement = tableSchema.Columns.Contains("IsAutoIncrement");
                bool isHasHidden = tableSchema.Columns.Contains("IsHidden");
                string hiddenFields = "," + AppConfig.DB.HiddenFields.ToLower() + ",";
                for (int i = 0; i < tableSchema.Rows.Count; i++)
                {
                    DataRow row = tableSchema.Rows[i];
                    tableName = Convert.ToString(row["BaseTableName"]);
                    mdcs.AddRelateionTableName(tableName);
                    if (isHasHidden && Convert.ToString(row["IsHidden"]) == "True")// !dcList.Contains(columnName))
                    {
                        continue;//后面那个会多出关联字段。
                    }
                    columnName = row["ColumnName"].ToString().Trim('"');//sqlite视图时会带引号
                    if (string.IsNullOrEmpty(columnName))
                    {
                        columnName = "Empty_" + i;
                    }
                    #region 处理是否隐藏列
                    bool isHiddenField = hiddenFields.IndexOf("," + columnName + ",", StringComparison.OrdinalIgnoreCase) > -1;
                    if (isHiddenField)
                    {
                        continue;
                    }
                    #endregion

                    bool.TryParse(Convert.ToString(row["IsKey"]), out isKey);
                    bool.TryParse(Convert.ToString(row["AllowDBNull"]), out isCanNull);
                    // isKey = Convert.ToBoolean();//IsKey
                    //isCanNull = Convert.ToBoolean(row["AllowDBNull"]);//AllowDBNull
                    if (isHasAutoIncrement)
                    {
                        isAutoIncrement = Convert.ToBoolean(row["IsAutoIncrement"]);
                    }
                    sqlTypeName = Convert.ToString(row[dataTypeName]);
                    sqlDbType = DataType.GetSqlType(sqlTypeName);

                    if (short.TryParse(Convert.ToString(row["NumericScale"]), out maxSizeScale) && maxSizeScale == 255)
                    {
                        maxSizeScale = 0;
                    }
                    if (!int.TryParse(Convert.ToString(row["NumericPrecision"]), out maxSize) || maxSize == 255)//NumericPrecision
                    {
                        long len;
                        if (long.TryParse(Convert.ToString(row["ColumnSize"]), out len))
                        {
                            if (len > int.MaxValue)
                            {
                                maxSize = int.MaxValue;
                            }
                            else
                            {
                                maxSize = (int)len;
                            }
                        }
                    }
                    MCellStruct mStruct = new MCellStruct(columnName, sqlDbType, isAutoIncrement, isCanNull, maxSize);
                    mStruct.Scale = maxSizeScale;
                    mStruct.IsPrimaryKey = isKey;
                    mStruct.SqlTypeName = sqlTypeName;
                    mStruct.TableName = tableName;
                    mStruct.OldName = mStruct.ColumnName;
                    mStruct.ReaderIndex = i;
                    mdcs.Add(mStruct);

                }
                tableSchema = null;
            }
            return mdcs;
        }
        private static MDataColumn GetViewColumns(string sqlText, ref DalBase helper)
        {
            helper.OpenCon(null, AllowConnLevel.MaterBackupSlave);
            helper.Com.CommandText = sqlText;
            DbDataReader sdr = helper.Com.ExecuteReader(CommandBehavior.KeyInfo);
            DataTable keyDt = null;
            if (sdr != null)
            {
                keyDt = sdr.GetSchemaTable();
                FixTableSchemaType(sdr, keyDt);
                sdr.Close();
            }
            MDataColumn mdc = GetColumns(keyDt);
            mdc.dalType = helper.DataBaseType;
            return mdc;

        }
        private static MDataColumn GetTxtDBViewColumns(string sqlText)
        {
            //sqlText=sqlText.ToLower();
            //List<string> tables = SqlFormat.GetTableNamesFromSql(sqlText);
            ////string key="select "
            //string selectItems=sqlText.Substring("select 
            return null;//暂未实现
        }
        public static void Clear()
        {
            _ColumnCache.Clear();
        }
    }
    internal partial class ColumnSchema
    {
        #region 表结构处理
        // private static CacheManage _SchemaCache = CacheManage.Instance;//Cache操作
        internal static bool FillTableSchema(ref MDataRow row, string tableName, string sourceTableName)
        {
            if (FillSchemaFromCache(ref row, tableName, sourceTableName))
            {
                return true;
            }
            else//从Cache加载失败
            {
                return FillSchemaFromDb(ref row, tableName, sourceTableName);
            }
        }

        /// <summary>
        /// 缓存表架构Key
        /// </summary>
        internal static string GetSchemaKey(string tableName, string conn)
        {
            //string key = tableName;
            //int start = key.IndexOf('(');
            //int end = key.LastIndexOf(')');
            //if (start > -1 && end > -1)//自定义table
            //{
            //    key = "View" + StaticTool.GetHashKey(key);
            //}
            //else
            //{
            //    key = SqlFormat.NotKeyword(key);
            //}
            return "ColumnsCache_" + ConnBean.GetHashCode(conn) + "_" + TableSchema.GetTableHash(tableName);
        }
        private static bool FillSchemaFromCache(ref MDataRow row, string tableName, string sourceTableName)
        {
            bool returnResult = false;

            string key = GetSchemaKey(tableName, row.Conn);
            if (CacheManage.LocalInstance.Contains(key))//缓存里获取
            {
                try
                {
                    row = ((MDataColumn)CacheManage.LocalInstance.Get(key)).ToRow(sourceTableName);
                    returnResult = row.Count > 0;
                }
                catch (Exception err)
                {
                    Log.Write(err, LogType.DataBase);
                }
            }
            else if (!string.IsNullOrEmpty(AppConfig.DB.SchemaMapPath))
            {
                string fullPath = AppConfig.RunPath + AppConfig.DB.SchemaMapPath + key + ".ts";
                if (System.IO.File.Exists(fullPath))
                {
                    MDataColumn mdcs = MDataColumn.CreateFrom(fullPath);
                    if (mdcs.Count > 0)
                    {
                        row = mdcs.ToRow(sourceTableName);
                        returnResult = row.Count > 0;
                        CacheManage.LocalInstance.Set(key, mdcs.Clone(), 1440);
                    }
                }
            }

            return returnResult;
        }
        private static bool FillSchemaFromDb(ref MDataRow row, string tableName, string sourceTableName)
        {
            try
            {
                MDataColumn mdcs = ColumnSchema.GetColumns(tableName, row.Conn);
                if (mdcs.Count == 0)
                {
                    return false;
                }
                row = mdcs.ToRow(sourceTableName);
                string key = GetSchemaKey(tableName, row.Conn);
                CacheManage.LocalInstance.Set(key, mdcs.Clone(), 1440);
                if (!string.IsNullOrEmpty(AppConfig.DB.SchemaMapPath))
                {
                    string folderPath = AppConfig.RunPath + AppConfig.DB.SchemaMapPath;
                    if (System.IO.Directory.Exists(folderPath))
                    {
                        mdcs.WriteSchema(folderPath + key + ".ts");
                    }
                }
                return true;

            }
            catch (Exception err)
            {
                Log.Write(err, LogType.DataBase);
                return false;
            }
        }
        #endregion
    }
    internal partial class ColumnSchema
    {
        #region 获取数据库表的列字段
        private const string MSSQL_SynonymsName = "SELECT TOP 1 base_object_name from sys.synonyms WHERE NAME = '{0}'";
        private const string Oracle_SynonymsName = "SELECT TABLE_NAME FROM USER_SYNONYMS WHERE SYNONYM_NAME='{0}' and rownum=1";
        internal static string GetMSSQLColumns(bool for2000, string dbName)
        {
            // 2005以上增加同义词支持。 case s2.name WHEN 'timestamp' THEN 'variant' ELSE s2.name END as [SqlType],
            return string.Format(@"select s1.name as ColumnName,case s2.name WHEN 'uniqueidentifier' THEN 36 
                     WHEN 'ntext' THEN -1 WHEN 'text' THEN -1 WHEN 'image' THEN -1 else s1.[prec] end  as [MaxSize],s1.scale as [Scale],
                     isnullable as [IsNullable],colstat&1 as [IsAutoIncrement],s2.name as [SqlType],
                     case when exists(SELECT 1 FROM {0}..sysobjects where xtype='PK' and name in (SELECT name FROM {0}..sysindexes WHERE id=s1.id and 
                     indid in(SELECT indid FROM {0}..sysindexkeys WHERE id=s1.id AND colid=s1.colid))) then 1 else 0 end as [IsPrimaryKey],
                     case when exists(SELECT 1 FROM {0}..sysobjects where xtype='UQ' and name in (SELECT name FROM {0}..sysindexes WHERE id=s1.id and 
                     indid in(SELECT indid FROM {0}..sysindexkeys WHERE id=s1.id AND colid=s1.colid))) then 1 else 0 end as [IsUniqueKey],
                     case when s5.rkey=s1.colid or s5.fkey=s1.colid then 1 else 0 end as [IsForeignKey],
                     case when s5.fkey=s1.colid then object_name(s5.rkeyid) else null end [FKTableName],
                     isnull(s3.text,'') as [DefaultValue],
                     s4.value as Description
                     from {0}..syscolumns s1 right join {0}..systypes s2 on s2.xtype =s1.xtype  
                     left join {0}..syscomments s3 on s1.cdefault=s3.id  " +
                     (for2000 ? "left join {0}..sysproperties s4 on s4.id=s1.id and s4.smallid=s1.colid  " : "left join {0}.sys.extended_properties s4 on s4.major_id=s1.id and s4.minor_id=s1.colid")
                     + " left join {0}..sysforeignkeys s5 on (s5.rkeyid=s1.id and s5.rkey=s1.colid) or (s5.fkeyid=s1.id and s5.fkey=s1.colid) where s1.id=object_id('{0}..'+@TableName) and s2.name<>'sysname' and s2.usertype<100 order by s1.colid", "[" + dbName + "]");
        }
        internal static string GetOracleColumns()
        {
            //同义词已被提取到外面执行，外部对表名已转大写，对语句进行了优化。
            return @"select A.COLUMN_NAME as ColumnName,case DATA_TYPE when 'DATE' then 23 when 'CLOB' then 2147483647 when 'NCLOB' then 1073741823 else case when CHAR_COL_DECL_LENGTH is not null then CHAR_COL_DECL_LENGTH
                    else   case when DATA_PRECISION is not null then DATA_PRECISION else DATA_LENGTH end   end end as MaxSize,DATA_SCALE as Scale,
                    case NULLABLE when 'Y' then 1 else 0 end as IsNullable,
                    0 as IsAutoIncrement,
                    case  when (DATA_TYPE='NUMBER' and DATA_SCALE>0 and DATA_PRECISION<13)  then 'float'
                      when (DATA_TYPE='NUMBER' and DATA_SCALE>0 and DATA_PRECISION<22)  then 'double'
                        when (DATA_TYPE='NUMBER' and DATA_SCALE=0 and DATA_PRECISION<11)  then 'int'
                          when (DATA_TYPE='NUMBER' and DATA_SCALE=0 and DATA_PRECISION<20)  then 'long'
                                when DATA_TYPE='NUMBER' then'decimal'                   
                    else DATA_TYPE end as SqlType,
                    case when v.CONSTRAINT_TYPE='P' then 1 else 0 end as IsPrimaryKey,
                      case when v.CONSTRAINT_TYPE='U' then 1 else 0 end as IsUniqueKey,
                        case when v.CONSTRAINT_TYPE='R' then 1 else 0 end as IsForeignKey,
                         case when length(r_constraint_name)>1
                         then (select table_name from user_constraints s where s.constraint_name=v.r_constraint_name)
                            else null
                              end as FKTableName ,
                    data_default as DefaultValue,
                    COMMENTS AS Description
                    from USER_TAB_COLS A left join user_col_comments B on A.Table_Name = B.Table_Name and A.Column_Name = B.Column_Name 
                    left join
                    (select uc1.table_name,ucc.column_name, uc1.constraint_type,uc1.r_constraint_name from user_constraints uc1
                    left join (SELECT column_name,constraint_name FROM user_cons_columns WHERE TABLE_NAME=:TableName) ucc on ucc.constraint_name=uc1.constraint_name
                    where uc1.constraint_type in('P','U','R') ) v
                    on A.TABLE_NAME=v.table_name and A.COLUMN_NAME=v.column_name
                    where A.TABLE_NAME=:TableName order by COLUMN_id";
            //            left join  user_constraints uc2 on uc1.r_constraint_name=uc2.constraint_name
            // where A.TABLE_NAME= nvl((SELECT TABLE_NAME FROM USER_SYNONYMS WHERE SYNONYM_NAME=UPPER(:TableName) and rownum=1),UPPER(:TableName)) order by COLUMN_id";
        }
        internal static string GetMySqlColumns(string dbName)
        {
            return string.Format(@"SELECT s1.COLUMN_NAME as ColumnName,case DATA_TYPE when 'int' then 10 when 'date' then 10 when 'time' then 8  when 'datetime' then 23 when 'year' then 4
                    else IFNULL(CHARACTER_MAXIMUM_LENGTH,NUMERIC_PRECISION) end as MaxSize,NUMERIC_SCALE as Scale,
                    case IS_NULLABLE when 'YES' then 1 else 0 end as IsNullable,
                    CASE extra when 'auto_increment' then 1 else 0 END AS IsAutoIncrement,
                    DATA_TYPE as SqlType,
                    case Column_key WHEN 'PRI' then 1 else 0 end as IsPrimaryKey,
                    case s3.CONSTRAINT_TYPE when 'UNIQUE' then 1 else 0 end as IsUniqueKey,
					case s3.CONSTRAINT_TYPE when 'FOREIGN KEY' then 1 else 0 end as IsForeignKey,
					s2.REFERENCED_TABLE_NAME as FKTableName,
                    COLUMN_DEFAULT AS DefaultValue,
                    COLUMN_COMMENT AS Description
                    FROM  `information_schema`.`COLUMNS` s1
					LEFT JOIN `information_schema`.KEY_COLUMN_USAGE s2 on s2.TABLE_SCHEMA=s1.TABLE_SCHEMA and s2.TABLE_NAME=s1.TABLE_NAME and s2.COLUMN_NAME=s1.COLUMN_NAME
					LEFT JOIN `information_schema`.TABLE_CONSTRAINTS s3 on s3.TABLE_SCHEMA=s2.TABLE_SCHEMA and s3.TABLE_NAME=s2.TABLE_NAME and s3.CONSTRAINT_NAME=s2.CONSTRAINT_NAME
                    where s1.TABLE_SCHEMA='{0}' and  s1.TABLE_NAME=?TableName order by s1.ORDINAL_POSITION", dbName);
        }
        internal static string GetSybaseColumns()
        {
            return @"select 
s1.name as ColumnName,
s1.length as MaxSize,
s1.scale as Scale,
case s1.status&8 when 8 then 1 ELSE 0 END AS IsNullable,
case s1.status&128 when 128 then 1 ELSE 0 END as IsAutoIncrement,
s2.name as SqlType,
case when exists(SELECT 1 FROM sysindexes WHERE id=s1.id AND s1.name=index_col(@TableName,indid,s1.colid)) then 1 else 0 end as IsPrimaryKey,
               str_replace(s3.text,'DEFAULT  ',null) as DefaultValue,
               null as Description
from syscolumns s1 left join systypes s2 on s1.usertype=s2.usertype
left join syscomments s3 on s1.cdefault=s3.id
where s1.id =object_id(@TableName) and s2.usertype<100
order by s1.colid";
        }
        internal static string GetPostgreColumns()
        {
            return @"select
a.attname AS ColumnName,
i.data_type AS SqlType,
coalesce(character_maximum_length,numeric_precision,-1) as MaxSize,numeric_scale as Scale,
case a.attnotnull when 'true' then 0 else 1 end AS IsNullable,
case  when position('nextval' in column_default)>0 then 1 else 0 end as IsAutoIncrement, 
case when o.conkey[a.attnum] is null then 0 else 1 end as IsPrimaryKey,
d.description AS Description,
i.column_default as DefaultValue
from pg_class c 
left join pg_attribute a on c.oid=a.attrelid
left join pg_description d on a.attrelid=d.objoid AND a.attnum = d.objsubid
left join pg_type t on a.atttypid = t.oid
left join information_schema.columns i on i.table_schema='public' and i.table_name=c.relname and i.column_name=a.attname
left join pg_constraint o on o.contype='p' and o.conrelid=c.oid
where c.relname =:TableName
and a.attnum > 0 and a.atttypid>0
ORDER BY a.attnum";
        }


        #endregion
    }
}
