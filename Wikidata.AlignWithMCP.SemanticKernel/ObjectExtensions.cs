//https://stackoverflow.com/a/4944547
using System.Reflection;

namespace Wikidata.AlignWithMCP.SemanticKernel;

public static class ObjectExtensions
{
    public static T ToObject<T>(this IDictionary<string, object> source)
        where T : class, new()
    {
        var someObject = new T();
        var someObjectType = someObject.GetType();

        foreach (var item in source)
        {
            someObjectType
                .GetProperty(item.Key)
                ?.SetValue(someObject, item.Value, null);
        }

        return someObject;
    }

    public static IDictionary<string, object?> AsDictionary(this object source, BindingFlags bindingAttr = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
    {
        return source.GetType().GetProperties(bindingAttr)
         .Where(propInfo => propInfo.GetIndexParameters().Length == 0) // Exclude indexers
         .ToDictionary
        (
            propInfo => propInfo.Name,
            propInfo => propInfo.GetValue(source, null)
        );

    }
}