﻿using System;
using System.IO;
using System.Web.Hosting;

namespace Kudu.Services.Web
{
    public static class PathResolver
    {
        public static string ResolveRootPath()
        {
            return Path.GetFullPath(ResolveRootPathInternal());
        }

        private static string ResolveRootPathInternal()
        {
            // If MapPath("/app") returns a valid folder, use it. This is the non-Azure code path
            string path = HostingEnvironment.MapPath(Constants.MappedSite);
            if (Directory.Exists(path))
            {
                return path;
            }

            // Due to an issue with d:\home on Azure, we can't quite use it yet
#if NOTYET
            // If d:\home exists, use it. This is a 'magic' folder on Azure that points to the root of the site files
            path = Environment.ExpandEnvironmentVariables(@"%SystemDrive%\home");
            if (Directory.Exists(path))
            {
                return path;
            }
#endif

            // Fall back to the HOME env variable, which is set on Azure but to a longer folder that looks
            // something like C:\DWASFiles\Sites\MySite\VirtualDirectory0
            path = Environment.ExpandEnvironmentVariables(@"%HOME%");
            if (Directory.Exists(path))
            {
                return path;
            }

            // We should never get here
            throw new DirectoryNotFoundException("The site's home directory could not be located");
        }
    }
}