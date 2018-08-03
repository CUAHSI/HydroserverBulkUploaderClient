using System;
using System.Collections.Generic;
using System.Text;

using System.Linq;
using System.Reflection;


namespace HydroserverBulkUploaderClient.Utilities
{
    public static class EnumUtil
    {
        //A simple extension method to retrieve the value of the Description attribute... 
        //Source: https://tech.io/playgrounds/2487/c---how-to-display-friendly-names-for-enumerations
        public static string GetDescription(this Enum GenericEnum)
        {
            Type genericEnumType = GenericEnum.GetType();
            MemberInfo[] memberInfo = genericEnumType.GetMember(GenericEnum.ToString());
            if ((memberInfo != null && memberInfo.Length > 0))
            {
                var _Attribs = memberInfo[0].GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);
                if ((_Attribs != null && _Attribs.Count() > 0))
                {
                    return ((System.ComponentModel.DescriptionAttribute)_Attribs.ElementAt(0)).Description;
                }
            }
            return GenericEnum.ToString();
        }
    }
}
