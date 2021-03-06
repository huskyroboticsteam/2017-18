﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Science_Base
{
    public partial class RailDisplay : Control
    {
        /// <summary> The location of the drill. </summary>
        /// 100 = Top
        /// 0 = Ground
        /// -20 = Fully in ground
        public int DrillLocation
        {
            get => this.P_DrillLocaton;
            set
            {
                this.P_DrillLocaton = value;
                Invalidate();
            }
        }
        private int P_DrillLocaton; // TODO: Adjust these value to reflect the diagram, and measure the physical properties of the science station.

        public bool ShowDistanceTop
        {
            get => this.P_ShowDistanceTop;
            set
            {
                this.P_ShowDistanceTop = value;
                Invalidate();
            }
        }
        private bool P_ShowDistanceTop;

        public bool ShowDistanceBottom
        {
            get => this.P_ShowDistanceBottom;
            set
            {
                this.P_ShowDistanceBottom = value;
                Invalidate();
            }
        }
        private bool P_ShowDistanceBottom;

        public byte InitStatus
        {
            get => this.P_InitStatus;
            set
            {
                this.P_InitStatus = value;
                Invalidate();
            }
        }
        private byte P_InitStatus;

        public RailDisplay()
        {
            this.ForeColor = Color.FromArgb(220, 220, 220);
            this.DrillLocation = 75;
            InitializeComponent();
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            base.OnPaint(pe);
            //pe.Graphics.FillRectangle(new SolidBrush(Color.Black), pe.ClipRectangle);

            SolidBrush Fore = new SolidBrush(this.ForeColor);

            Rectangle DiagramArea = ShrinkRect(pe.ClipRectangle, 0, 0, 15, 0); // Leaves space for the height indicators

            Rectangle TopBar = new Rectangle(DiagramArea.Location, new Size(DiagramArea.Width, 5));
            pe.Graphics.FillRectangle(Fore, TopBar);

            Rectangle VerticalBar = new Rectangle(MovePt(DiagramArea.Location, (int)(DiagramArea.Width * 0.2F), 3), new Size(2, (int)(DiagramArea.Height * 0.7F)));
            pe.Graphics.FillRectangle(Fore, VerticalBar);

            int GroundHeight = (int)(DiagramArea.Height * 0.75F); // Top of the ground line
            Rectangle GroundLine = new Rectangle(MovePt(DiagramArea.Location, 0, GroundHeight), new Size(DiagramArea.Width, 2));
            pe.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(0x96, 0x3F, 0x2D)), GroundLine);

            int DrillHeight = (int)(DiagramArea.Height * 0.2F); // How tall the drill picture is
            Rectangle DrillTop = new Rectangle(MovePt(VerticalBar.Location, 2, (int)((GroundHeight - (TopBar.Location.Y + TopBar.Height) - 2) * (100 - this.DrillLocation) / 100.0F) + 2), new Size(DiagramArea.Width / 2, 2));
            pe.Graphics.FillRectangle(Fore, DrillTop);

            Rectangle DrillBounds = new Rectangle(MovePt(DrillTop.Location, (int)(DrillTop.Width * 0.6F), 2), new Size((int)(DrillTop.Width * 0.2F), DrillHeight));
            Color DrillColor;
            if (this.InitStatus == 1) { DrillColor = Color.Yellow; }
            else if (this.InitStatus == 2) { DrillColor = Color.Green; }
            else { DrillColor = Color.Red; }

            Pen DrillPen = new Pen(DrillColor, 1);
            int BottomStraight = DrillBounds.Bottom - (DrillBounds.Width / 2); // Top of where the drill begins the v shape at the bottom.
            pe.Graphics.DrawLine(DrillPen, DrillBounds.Left, DrillBounds.Top, DrillBounds.Left, BottomStraight); // Left
            pe.Graphics.DrawLine(DrillPen, DrillBounds.Right, DrillBounds.Top, DrillBounds.Right, BottomStraight); // Right
            pe.Graphics.DrawLine(DrillPen, DrillBounds.Left, BottomStraight, (DrillBounds.Left + (DrillBounds.Width / 2)), DrillBounds.Bottom); // Left v
            pe.Graphics.DrawLine(DrillPen, DrillBounds.Right, BottomStraight, (DrillBounds.Right - (DrillBounds.Width / 2)), DrillBounds.Bottom); // Right v

            if (this.ShowDistanceTop)
            {
                pe.Graphics.DrawLine(new Pen(Color.Green, 2), (DiagramArea.Right + 7), TopBar.Bottom, (DiagramArea.Right + 7), DrillTop.Y); // Vertical Line
                pe.Graphics.DrawLine(new Pen(Color.Green, 2), DiagramArea.Right, TopBar.Bottom, (DiagramArea.Right + 15), TopBar.Bottom); // Top Bar
                pe.Graphics.DrawLine(new Pen(Color.Green, 2), DiagramArea.Right, DrillTop.Y, (DiagramArea.Right + 15), DrillTop.Y); // Bottom Bar
            }

            if (this.ShowDistanceBottom)
            {
                Pen Pen = new Pen((((DrillTop.Y + DrillHeight) < GroundHeight) ? Color.Green : Color.Red), 2);
                pe.Graphics.DrawLine(Pen, (DiagramArea.Right + 7), (DrillTop.Y + DrillHeight), (DiagramArea.Right + 7), GroundHeight); // Vertical Line
                pe.Graphics.DrawLine(Pen, DiagramArea.Right, GroundHeight, (DiagramArea.Right + 15), GroundHeight); // Bottom Bar
                pe.Graphics.DrawLine(Pen, DiagramArea.Right, (DrillTop.Y + DrillHeight), (DiagramArea.Right + 15), (DrillTop.Y + DrillHeight)); // Top Bar
            }
        }

        private Rectangle ShrinkRect(Rectangle Input, int Left, int Top, int Right, int Bottom)
        {
            Point Loc = new Point(Input.Location.X + Left, Input.Location.Y + Top);
            Size Size = new Size(Input.Size.Width - (Left + Right), Input.Size.Height - (Top + Bottom));
            Rectangle Rect = new Rectangle(Loc, Size);
            return Rect;
        }

        private Point MovePt(Point Point, int X, int Y)
        {
            Point.Offset(X, Y);
            return Point;
        }
    }
}
