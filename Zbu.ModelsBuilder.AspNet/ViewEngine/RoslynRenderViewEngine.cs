﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using Umbraco.Core;
using Umbraco.Core.IO;
using Umbraco.Web.Models;

namespace Zbu.ModelsBuilder.AspNet.ViewEngine
{
    // RoslynRenderViewEngine: a replacement for Umbraco RenderViewEngine
    //
    // not inheriting Umbraco's RenderViewEngine which is weirdish, supporting
    // MVC 4 and 5, not implementing CreateView... re-implementing it all here
    // in pure-MVC 5 style
    //
    // is: a view engine to look into the template location specified in the
    // config for the front-end/rendering part of the cms - this includes paths
    // to render partial macros and media item templates.
    //
    // only deals with Umbraco views, anything that's "standard" MVC is taken
    // care of by the standard RazorViewEngine - so no need to check anything
    // here

    public class RoslynRenderViewEngine : RoslynViewEngineBase
    {
        private readonly IEnumerable<string> _supplementedViewLocations = new[] { "/{0}.cshtml" };

        // NOTE: we will make the main view location the last to be searched since if it is the first to be searched and there is both a view and a partial
        // view in both locations and the main view is rendering a partial view with the same name, we will get a stack overflow exception. 
        // http://issues.umbraco.org/issue/U4-1287, http://issues.umbraco.org/issue/U4-1215
        private readonly IEnumerable<string> _supplementedPartialViewLocations = new[] { "/Partials/{0}.cshtml", "/MacroPartials/{0}.cshtml", "/{0}.cshtml" };

        public RoslynRenderViewEngine()
        {
			const string templateFolder = UmbracoInternals.ViewLocation;

			var replaceWithUmbracoFolder = _supplementedViewLocations.ForEach(location => templateFolder + location);
			var replacePartialWithUmbracoFolder = _supplementedPartialViewLocations.ForEach(location => templateFolder + location);

			// the Render view engine doesn't support Area's so make those blank
            ViewLocationFormats = replaceWithUmbracoFolder.ToArray();
			PartialViewLocationFormats = replacePartialWithUmbracoFolder.ToArray();

			AreaPartialViewLocationFormats = new string[] { };
			AreaViewLocationFormats = new string[] { };

			EnsureFoldersAndFiles();
        }

        #region Umbraco Rendering

        // this is mostly copied over from Umbraco's internals

        private static void EnsureFoldersAndFiles()
        {
            var viewFolder = IOHelper.MapPath(UmbracoInternals.ViewLocation);

            //ensure the web.config file is in the ~/Views folder
            Directory.CreateDirectory(viewFolder);
            if (!File.Exists(Path.Combine(viewFolder, "web.config")))
            {
                using (var writer = File.CreateText(Path.Combine(viewFolder, "web.config")))
                {
                    writer.Write(UmbracoInternals.WebConfigTemplate);
                }
            }

            //auto create the partials folder
            var partialsFolder = Path.Combine(viewFolder, "Partials");
            Directory.CreateDirectory(partialsFolder);

            //We could create a _ViewStart page if it isn't there as well, but we may not allow editing of this page in the back office.
        }

        public override ViewEngineResult FindView(ControllerContext controllerContext, string viewName, string masterName, bool useCache)
        {
            return ShouldFindView(controllerContext, false) 
                ? base.FindView(controllerContext, viewName, masterName, useCache)
                : new ViewEngineResult(new string[] { });
        }

        public override ViewEngineResult FindPartialView(ControllerContext controllerContext, string partialViewName, bool useCache)
        {
            return ShouldFindView(controllerContext, true) 
                ? base.FindPartialView(controllerContext, partialViewName, useCache)
                : new ViewEngineResult(new string[] { });
        }

        // determines if the view should be found, this is used for view lookup performance and also to ensure 
        // less overlap with other user's view engines. This will return true if the Umbraco back office is rendering
        // and its a partial view or if the umbraco front-end is rendering but nothing else.
        private static bool ShouldFindView(ControllerContext controllerContext, bool isPartial)
        {
            var umbracoToken = UmbracoInternals.GetDataTokenInViewContextHierarchy(controllerContext, "umbraco");

            //first check if we're rendering a partial view for the back office, or surface controller, etc...
            //anything that is not IUmbracoRenderModel as this should only pertain to Umbraco views.
            if (isPartial && ((umbracoToken is RenderModel) == false))
                return true;

            //only find views if we're rendering the umbraco front end
            return (umbracoToken is RenderModel);
        }

        #endregion
    }
}