using System;
using System.Drawing;
using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;

using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.Controls;
using ESRI.ArcGIS.ADF;
using ESRI.ArcGIS.SystemUI;
using ESRI.ArcGIS.Geodatabase;
using System.Diagnostics;
using ESRI.ArcGIS.Geometry;

namespace QueryByLayer
{
    public sealed partial class MainForm : Form
    {
        #region class private members
        private IMapControl3 m_mapControl = null;
        private string m_mapDocumentName = string.Empty;
        string mxdPath = Application.StartupPath + "\\test.mxd";
        #endregion

        #region class constructor
        public MainForm()
        {
            InitializeComponent();
        }
        #endregion

        private void MainForm_Load(object sender, EventArgs e)
        {
            //get the MapControl
            m_mapControl = (IMapControl3)axMapControl1.Object;

            //disable the Save menu (since there is no document yet)
            menuSaveDoc.Enabled = false;

            if(m_mapControl.CheckMxFile(mxdPath))
            {
                m_mapControl.LoadMxFile(mxdPath);
            }

        }

        #region Main Menu event handlers
        private void menuNewDoc_Click(object sender, EventArgs e)
        {
            //execute New Document command
            ICommand command = new CreateNewDocument();
            command.OnCreate(m_mapControl.Object);
            command.OnClick();
        }

        private void menuOpenDoc_Click(object sender, EventArgs e)
        {
            //execute Open Document command
            ICommand command = new ControlsOpenDocCommandClass();
            command.OnCreate(m_mapControl.Object);
            command.OnClick();
        }

        private void menuSaveDoc_Click(object sender, EventArgs e)
        {
            //execute Save Document command
            if (m_mapControl.CheckMxFile(m_mapDocumentName))
            {
                //create a new instance of a MapDocument
                IMapDocument mapDoc = new MapDocumentClass();
                mapDoc.Open(m_mapDocumentName, string.Empty);

                //Make sure that the MapDocument is not readonly
                if (mapDoc.get_IsReadOnly(m_mapDocumentName))
                {
                    MessageBox.Show("Map document is read only!");
                    mapDoc.Close();
                    return;
                }

                //Replace its contents with the current map
                mapDoc.ReplaceContents((IMxdContents)m_mapControl.Map);

                //save the MapDocument in order to persist it
                mapDoc.Save(mapDoc.UsesRelativePaths, false);

                //close the MapDocument
                mapDoc.Close();
            }
        }

        private void menuSaveAs_Click(object sender, EventArgs e)
        {
            //execute SaveAs Document command
            ICommand command = new ControlsSaveAsDocCommandClass();
            command.OnCreate(m_mapControl.Object);
            command.OnClick();
        }

        private void menuExitApp_Click(object sender, EventArgs e)
        {
            //exit the application
            Application.Exit();
        }
        #endregion

        //listen to MapReplaced evant in order to update the statusbar and the Save menu
        private void axMapControl1_OnMapReplaced(object sender, IMapControlEvents2_OnMapReplacedEvent e)
        {
            //get the current document name from the MapControl
            m_mapDocumentName = m_mapControl.DocumentFilename;

            //if there is no MapDocument, diable the Save menu and clear the statusbar
            if (m_mapDocumentName == string.Empty)
            {
                menuSaveDoc.Enabled = false;
                statusBarXY.Text = string.Empty;
            }
            else
            {
                //enable the Save manu and write the doc name to the statusbar
                menuSaveDoc.Enabled = true;
                statusBarXY.Text = System.IO.Path.GetFileName(m_mapDocumentName);
            }
        }

        private void axMapControl1_OnMouseMove(object sender, IMapControlEvents2_OnMouseMoveEvent e)
        {
            statusBarXY.Text = string.Format("{0}, {1}  {2}", e.mapX.ToString("#######.##"), e.mapY.ToString("#######.##"), axMapControl1.MapUnits.ToString().Substring(4));
        }

        //功能：查询点要素类中所有包含在面要素类内部的点要素
        private void iSpatialFilterOneByOneToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Stopwatch myWatch = Stopwatch.StartNew();
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
                spatialFilter.SpatialRel = esriSpatialRelEnum.esriSpatialRelContains;
                pointFeatureSelection.SelectFeatures(spatialFilter as IQueryFilter, esriSelectionResultEnum.esriSelectionResultAdd, false);
               
            }
            int count = pointFeatureSelection.SelectionSet.Count;
            axMapControl1.Refresh();
            //释放游标
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(cursor);
            myWatch.Stop();
            string time = myWatch.Elapsed.TotalSeconds.ToString();
            MessageBox.Show("The selected point count is " + count.ToString() + "! and " + time + " Seconds");
        }

        private void iSpatialFilterSpatialCacheToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Stopwatch myWatch = Stopwatch.StartNew();
            //从MapControl中获取的点图层
            IFeatureLayer pointFeatureLayer = axMapControl1.get_Layer(0) as IFeatureLayer;
            IFeatureSelection pointFeatureSelection = pointFeatureLayer as IFeatureSelection;
            //从MapControl中获取的面图层
            IFeatureLayer polygonFeatureLayer = axMapControl1.get_Layer(1) as IFeatureLayer;
            //填充Spatial Cache
            ISpatialCacheManager spatialCacheManager = (ISpatialCacheManager)(pointFeatureLayer as IDataset).Workspace;
            IEnvelope cacheExtent = (pointFeatureLayer as IGeoDataset).Extent;
            //检测是否存在缓存
            if (!spatialCacheManager.CacheIsFull)
            {
                //不存在，则创建缓存
                spatialCacheManager.FillCache(cacheExtent);
            }

            //构建缓存后进行查询
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
                spatialFilter.SpatialRel = esriSpatialRelEnum.esriSpatialRelContains;
                //选择的话可以不设置SubFields               
                pointFeatureSelection.SelectFeatures(spatialFilter as IQueryFilter, esriSelectionResultEnum.esriSelectionResultAdd, false);
                
            }
            int count = pointFeatureSelection.SelectionSet.Count;
            //清空空间缓存
            spatialCacheManager.EmptyCache();            
            axMapControl1.Refresh();
            //释放游标
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(cursor);
            myWatch.Stop();
            string time = myWatch.Elapsed.TotalSeconds.ToString();
            MessageBox.Show("The selected point count is " + count.ToString() + "! and " + time + " Seconds");
        }

        private void iSpatialFilterGeometryBagToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Stopwatch myWatch = Stopwatch.StartNew();

            //从MapControl中获取的点图层
            IFeatureLayer pointFeatureLayer = axMapControl1.get_Layer(0) as IFeatureLayer;
            IFeatureSelection pointFeatureSelection = pointFeatureLayer as IFeatureSelection;
            //从MapControl中获取的面图层
            IFeatureLayer polygonFeatureLayer = axMapControl1.get_Layer(1) as IFeatureLayer;

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

            int count = pointFeatureSelection.SelectionSet.Count;

            axMapControl1.Refresh();
            //释放游标
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(cursor);

            myWatch.Stop();
            string time = myWatch.Elapsed.TotalSeconds.ToString();
            MessageBox.Show("The selected point count is " + count.ToString() + "! and " + time + " Seconds");
        }

        private void iQueryByLayerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Stopwatch myWatch = Stopwatch.StartNew();

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
            int count = pointFeatureSelection.SelectionSet.Count;

            axMapControl1.Refresh();

            myWatch.Stop();
            string time = myWatch.Elapsed.TotalSeconds.ToString();
            MessageBox.Show("The selected point count is " + count.ToString() + "! and " + time + " Seconds");
        }

      
    }
}