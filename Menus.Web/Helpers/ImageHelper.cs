using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Menus.Web.Helpers
{
    public static class ImageHelper
    {
        public static string GetMimeType(string base64)
        {
            string mimeType = "image/png";
            if (base64.IndexOf("image/png") > -1)
            {
                mimeType = "image/png";
            }
            else
            if (base64.IndexOf("image/jpeg") > -1)
            {
                mimeType = "image/jpeg";
            }

            return mimeType;
        }

        public static string GetExtension(string base64)
        {
            string extinsion = ".png";
            if (base64.IndexOf("image/png") > -1)
            {
                extinsion = ".png";
            }
            else
            if (base64.IndexOf("image/jpeg") > -1)
            {
                extinsion = ".jpeg";
            }

            return extinsion;
        }
    }
}
