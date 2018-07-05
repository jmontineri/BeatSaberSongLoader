using System.Reflection;

namespace SongLoaderPlugin
{
	public static class ReflectionUtil
	{
		public static void SetPrivateField(object obj, string fieldName, object value)
		{
			FieldInfo prop = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
			prop.SetValue(obj, value);
		}
		
		public static T GetPrivateField<T>(object obj, string fieldName)
		{
			FieldInfo prop = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
			object value = prop.GetValue(obj);
			return (T) value;
		}
		
		public static void SetPrivateProperty(object obj, string propertyName, object value)
		{
			PropertyInfo prop = obj.GetType().GetProperty(propertyName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
			prop.SetValue(obj, value, null);
		}

		public static void InvokePrivateMethod(object obj, string methodName, object[] methodParams)
		{
			MethodInfo dynMethod = obj.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
			dynMethod.Invoke(obj, methodParams);
		}
	}
}
