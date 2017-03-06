# QueryByLayer

如果您是ArcGIS Engine开发人员，也许会有这样的困惑：为什么对两个要素图层进行空间选择，ArcMap中瞬间就出结果了，而Engine中则慢很多倍，尤其是当数据量大时，该速率甚至无法忍受。图层间如何实现高效的空间选择呢？相信阅读完下面的文章，答案会迎刃而解。

下面就带着问题来开始今天的讨论吧。

##问题：

###假如有一个居民点数据和一个建筑物数据，想要知道哪些居民点被建筑物所覆盖，如何实现？

##答案：

###ArcMap中如何实现？

ArcMap中实现此功能很简单，即使用菜单条上的Select By Location或者[Select Layer By Location](http://resources.arcgis.com/en/help/main/10.2/index.html#//001700000072000000)工具，数据（点要素类中含有600个点，面要素类中含有750个面）和结果如下图所示。

![ArcMap中](http://img.blog.csdn.net/20170306152204841?watermark/2/text/aHR0cDovL2Jsb2cuY3Nkbi5uZXQveGlueWluZzE4MA==/font/5a6L5L2T/fontsize/400/fill/I0JBQkFCMA==/dissolve/70/gravity/SouthEast)

**用时：0.06秒**

###ArcGIS Engine中如何实现呢？ 

ArcGIS Engine中（相信很多用户是不太喜欢直接调用GP工具的）如何实现该功能呢？一般情况下会考虑使用[ISpatialFilter](http://resources.arcgis.com/en/help/arcobjects-net/componenthelp/index.html#//00250000083n000000)结合遍历面要素来实现：

ISpatialFilter可以进行空间查询和属性查询。其Geometry属性传入要查询的几何，仅可使用high-level geometry（如polygon、polyline、points、multipoints）、envelope和geometry bags，这里使用面要素的geometry；其GeometryField属性用于指定过滤所使用的几何字段，这里使用点要素类的几何字段pointFeatureClass.ShapeFieldName；其SpatialRel属性用于指定空间关系，公式为[query_geometry] [spatial_relationship] [feature]，这里的query_geometry为面，feature为点要素，空间关系就应该是包含，所以应该使用**esriSpatialRelContains**（*注意与ArcMap中有所区别，ArcMap中需设置空间关系为**CompletelyWithin***）。SubFields可以指定查询后返回的属性字段，以逗号分隔。如果不设置的话会默认为“*”，即返回所有字段。如果需要获取要素属性值得话，最好设置该字段，可以提高性能。当然如果不获取属性的话，那这个字段可设可不设，比如选择要素。此外，如果还想同时进行属性查询，直接设置WhereClause语句即可。主要代码：

```
            //从MapControl中获取的点图层
            IFeatureLayer pointFeatureLayer = axMapControl1.get_Layer(0) as IFeatureLayer;
            IFeatureSelection pointFeatureSelection = pointFeatureLayer as IFeatureSelection;
            //从MapControl中获取的面图层
            IFeatureLayer polygonFeatureLayer = axMapControl1.get_Layer(1) as IFeatureLayer;
            //循环遍历面要素类内部的面，逐一进行查询
            IQueryFilter queryFilter = new QueryFilterClass();
            //Search如果返回属性值的话设置SubFields会提高效率
            queryFilter.SubFields = "Shape";
            IFeatureCursor cursor = polygonFeatureLayer.Search(queryFilter, true);
            IFeature polygonFeature = null;
            while ((polygonFeature = cursor.NextFeature()) != null)
            {
                IGeometry queryGeometry = polygonFeature.Shape;
                //构建空间查询
                ISpatialFilter spatialFilter = new SpatialFilterClass();
                spatialFilter.Geometry = queryGeometry;
                spatialFilter.GeometryField = pointFeatureLayer.FeatureClass.ShapeFieldName;
                spatialFilter.SpatialRel = esriSpatialRelEnum.esriSpatialRelContains;                pointFeatureSelection.SelectFeatures(spatialFilter as IQueryFilter, esriSelectionResultEnum.esriSelectionResultAdd, false);              
            }     
//释放游标          
System.Runtime.InteropServices.Marshal.FinalReleaseComObject(cursor);
```

**用时：1.45秒**

怎么和ArcMap中效率差这么多啊，别急，下面就来看优化方法。

###**优化1：创建空间缓存**

使用[ISpatialCacheManager](http://resources.arcgis.com/en/help/arcobjects-net/componenthelp/index.html#//002500000831000000)接口对要素类创建空间缓存，然后使用上面的方法遍历选择。如果对同一个范围进行多次空间查询的话，先构建空间缓存会提升效率，因为其降低了数据库的访问次数，比如下面场景，当然也很适用于我们的问题。

![这里写图片描述](http://img.blog.csdn.net/20170306152922943?watermark/2/text/aHR0cDovL2Jsb2cuY3Nkbi5uZXQveGlueWluZzE4MA==/font/5a6L5L2T/fontsize/400/fill/I0JBQkFCMA==/dissolve/70/gravity/SouthEast)

使用方法就是打开进行查询的要素类，为该范围创建空间缓存，执行查询，最后释放缓存。主要代码：

```
            //填充Spatial Cache
            ISpatialCacheManager spatialCacheManager = (ISpatialCacheManager)(pointFeatureLayer as IDataset).Workspace;
            IEnvelope cacheExtent = (pointFeatureLayer as IGeoDataset).Extent;
            //检测是否存在缓存
            if (!spatialCacheManager.CacheIsFull)
            {
                //不过不存在，则创建缓存
                spatialCacheManager.FillCache(cacheExtent);
            }

            //执行空间查询操作，与上文一样
            ...            
            //清空空间缓存
            spatialCacheManager.EmptyCache(); 

```

**用时：0.45秒**

可见，创建空间缓存后效率有所提升，但与ArcMap中调用GP相比，效率还差了几乎一个数量级。怎么办？

###**优化2：使用[IGeometryBag](http://resources.arcgis.com/en/help/arcobjects-net/componenthelp/index.html#//002m000001s8000000)接口**

细心的你也许会发现上述方法之所以慢，是因为执行了多次SelectFeatures方法，如果只执行一次，肯定会快很多。而我们开始介绍ISpatialFilter的Geometry属性时提到过，Geometry可以传入GeometryBag，那么GeometryBag是什么呢？GeometryBag是Geometry的集合，可以往里添加N个Geometry，好吧，既然GeometryBag是个集合，那我们就可以把遍历的面都添加进去，这样就可以只执行一次SelectFeatures，很显然可以提高效率。不过使用GeometryBag时有一点需要注意：**必须为其设置空间参考**，因为添加Geometry（即使原本有空间参考）到GeometryBag中时会丢失空间参考。此外，如果**为该GeometryBag创建空间索引会提高效率**。主要代码：

```
            //构建GeometryBag
            IGeometryBag geometryBag = new GeometryBagClass();
            IGeometryCollection geometryCollection = (IGeometryCollection)geometryBag;
            IGeoDataset geoDataset = (IGeoDataset)polygonFeatureLayer;
            ISpatialReference spatialReference = geoDataset.SpatialReference;
            //一定要给GeometryBag赋空间参考
            geometryBag.SpatialReference = spatialReference;            
            IQueryFilter queryFilter = new QueryFilterClass();
            //Search如果返回属性值的话设置SubFields会提高效率
            queryFilter.SubFields = "Shape";           
            //遍历面要素类，逐一获取Geometry并添加到GeometryBag中
            IFeatureCursor cursor = polygonFeatureLayer.Search(queryFilter, true);
            IFeature polygonFeature = null;
            while ((polygonFeature = cursor.NextFeature()) != null)
            {
                geometryCollection.AddGeometry(polygonFeature.ShapeCopy);
            }
            //为GeometryBag生成空间索引，以提高效率
            ISpatialIndex spatialIndex = (ISpatialIndex)geometryBag;
            spatialIndex.AllowIndexing = true;
            spatialIndex.Invalidate();
            //构建空间查询
            ISpatialFilter spatialFilter = new SpatialFilterClass();
            spatialFilter.Geometry = geometryBag;
            spatialFilter.GeometryField = pointFeatureLayer.FeatureClass.ShapeFieldName;
            spatialFilter.SpatialRel = esriSpatialRelEnum.esriSpatialRelContains;
            //选择的话可以不设置SubFields
            pointFeatureSelection.SelectFeatures(spatialFilter as IQueryFilter, esriSelectionResultEnum.esriSelectionResultAdd, false);   

```

**用时：0.045秒**

喜大普奔！该方法居然比ArcMap中直接调用GP还要快？！可是我觉得代码稍显复杂，有没有更简化而且高效的方法呢？（居然还想要自行车…）

###**优化3：使用[IQueryByLayer](http://resources.arcgis.com/en/help/arcobjects-net/componenthelp/index.html#//002m000001s8000000)接口**

IQueryByLayer接口就是用来进行图层间空间选择的！！！其FromLayer属性是指对哪个图层进行选择，这里即为点图层；其ByLayer属性是指查询几何所在的图层，即面图层；其LayerSelectionMethod就是两个图层间的空间关系，这里是**esriLayerSelectCompletelyWithin**（细心的同学注意到了吧，空间关系和ArcMap中使用的一模一样，ArcMap甚有可能就是用的该接口）；此外还有一个UseSelectedFeatures属性很重要，我开始没有设置，结果导致程序一直报下面的错误：

![报错](http://img.blog.csdn.net/20170306160513870?watermark/2/text/aHR0cDovL2Jsb2cuY3Nkbi5uZXQveGlueWluZzE4MA==/font/5a6L5L2T/fontsize/400/fill/I0JBQkFCMA==/dissolve/70/gravity/SouthEast)

该属性是说ByLayer中是否进行了选择，如果面图层中没有选中要素的话，一定要设置为该属性为false，如果设为true或者不设置都会报这个错。但是，如果面图层中进行了选中，想用选中的面进行空间查询，就要将其设为true了，如果设为false是按整个面要素类进行查询的，如果不设置是按选中的面进行查询的，所以我认为该属性的默认值为true，但AO帮助中并没有说明。主要代码：

```
            //从MapControl中获取的点图层
            IFeatureLayer pointFeatureLayer = axMapControl1.get_Layer(0) as IFeatureLayer;
            IFeatureSelection pointFeatureSelection = pointFeatureLayer as IFeatureSelection;
            //从MapControl中获取的面图层
            IFeatureLayer polygonFeatureLayer = axMapControl1.get_Layer(1) as IFeatureLayer;
            //构建QueryByLayer
            IQueryByLayer queryByLayer = new QueryByLayerClass();
            queryByLayer.FromLayer = pointFeatureLayer;
            queryByLayer.ByLayer = polygonFeatureLayer;
            queryByLayer.LayerSelectionMethod = esriLayerSelectionMethod.esriLayerSelectCompletelyWithin;
            //该参数需要设置
            queryByLayer.UseSelectedFeatures = false;
            ISelectionSet selectionSet = queryByLayer.Select();
            pointFeatureSelection.SelectionSet = selectionSet;
axMapControl1.Refresh();

```

**用时：0.038秒**

这种方法的代码比上面那种简单的多，而且用时也更少，这就是我认为既简单又高效的方法。由上可见，ArcGIS Engine中进行图层间的空间选择，方法使用正确了，确实会与ArcMap效率相当，甚至还要更快哦！

###**总结一下：**

本文仅以图层间的空间选择为例进行了优化，其实在进行空间查询时也可以采用文中的思想，比如：

1，	对同一区域进行多次查询时可以使用ISpatialCacheManager创建空间缓存；
2，	要使用多个geometry进行空间查询时，使用GeometryBag会提高效率；
3，	图层间的查询也是可以转化为空间选择的，使用IQueryByLayer接口获取[ISelectionSet](http://resources.arcgis.com/en/help/arcobjects-net/componenthelp/index.html#//002500000801000000)，进而获取到所有的要素；
4，	多次使用IRelationalOperator或者ITopologicalOperator接口进行空间关系的判断时也可以使用GeometryBag，但具体需要看所使用的方法是否支持GeometryBag。

##Demo

使用ArcGIS Engine 10.5，Visual Studio 2015编写，创建了MapControlApplication模版工程，然后添加菜单栏，分别使用了上面提到的方法进行测试。界面为：

![程序界面](http://img.blog.csdn.net/20170306162116467?watermark/2/text/aHR0cDovL2Jsb2cuY3Nkbi5uZXQveGlueWluZzE4MA==/font/5a6L5L2T/fontsize/400/fill/I0JBQkFCMA==/dissolve/70/gravity/SouthEast)

*Tips：*测试时是直接加载的已有mxd，然后获取的图层，如果直接从数据源获取要素类，然后创建图层，再查询会相对慢些。

###工程下载地址：

[QueryByLayer](https://github.com/xinying180/QueryByLayer)
