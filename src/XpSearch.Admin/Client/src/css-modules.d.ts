// css-loader turns a *.module.css import into a class-name map; TypeScript needs to be told.
declare module '*.module.css' {
  const classes: Readonly<Record<string, string>>;

  export default classes;
}
