const webpackMerge = require("webpack-merge");

const baseWebpackConfig = require("@kentico/xperience-webpack-config");

// The admin client module boilerplate, unchanged except for orgName/projectName:
// https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/prepare-your-environment-for-admin-development
module.exports = (opts, argv) => {
  const baseConfig = (webpackConfigEnv, argv) => {
    return baseWebpackConfig({
      // Must match RegisterClientModule("xperience-community", "xperience-search") in
      // XpSearchAdminClientModule.cs and AdminOrgName/ProjectName in XpSearch.Admin.csproj.
      orgName: "xperience-community",
      projectName: "xperience-search",
      webpackConfigEnv: webpackConfigEnv,
      argv: argv,
    });
  };

  const projectConfig = {
    module: {
      rules: [
        {
          test: /\.(js|ts)x?$/,
          exclude: [/node_modules/],
          loader: "babel-loader",
        },
        // css-loader turns *.module.css into CSS modules on its own, so page styles stay scoped.
        // Extending the boilerplate config with CSS loaders is Kentico's documented route:
        // https://docs.kentico.com/documentation/developers-and-admins/configuration/rich-text-editor-configuration/rich-text-editor-customization
        {
          test: /\.css$/,
          // css-loader 7 defaults CSS modules to named exports; namedExport: false keeps the
          // `import styles from "./x.module.css"` default-import shape the templates use.
          use: [
            "style-loader",
            {
              loader: "css-loader",
              options: { modules: { auto: true, namedExport: false, exportLocalsConvention: "as-is" } },
            },
          ],
        },
      ],
    },
    output: {
      clean: true,
    },
    devServer: {
      port: 3010,
    },
  };

  return webpackMerge.merge(projectConfig, baseConfig(opts, argv));
};
