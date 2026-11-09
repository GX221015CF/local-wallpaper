"""Draw original operation diagrams. These are schematic illustrations, not app screenshots."""
from pathlib import Path
from html import escape
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'docs/user-guide/教程资源'
ASSETS.mkdir(parents=True, exist_ok=True)
FONT = Path(os.environ['WINDIR']) / 'Fonts/msyh.ttc'
INK = '#172b4d'
MUTED = '#53647e'
BLUE = '#245bd6'


class Figure:
    def __init__(self, title, height=460):
        self.image = Image.new('RGB', (1120, height), '#edf3fc')
        self.draw = ImageDraw.Draw(self.image)
        self.svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="1120" height="{height}" viewBox="0 0 1120 {height}">',
                    f'<rect width="1120" height="{height}" fill="#edf3fc"/>']
        self.text(32, 22, title, 25)
        self.rect(928, 20, 1088, 62, '#dce7fc')
        self.text(948, 27, '操作示意', 20, BLUE)

    def rect(self, x1, y1, x2, y2, fill='white', outline=None):
        self.draw.rounded_rectangle((x1, y1, x2, y2), radius=12, fill=fill, outline=outline, width=2)
        self.svg.append(f'<rect x="{x1}" y="{y1}" width="{x2-x1}" height="{y2-y1}" rx="12" fill="{fill}" stroke="{outline or fill}" stroke-width="2"/>')

    def text(self, x, y, value, size=20, color=INK):
        self.draw.text((x, y), value, font=ImageFont.truetype(str(FONT), size), fill=color)
        self.svg.append(f'<text x="{x}" y="{y+size}" font-family="Microsoft YaHei, sans-serif" font-size="{size}" fill="{color}">{escape(value)}</text>')

    def button(self, x, y, width, value, primary=False):
        self.rect(x, y, x+width, y+46, BLUE if primary else '#eaf0fa')
        self.text(x+14, y+9, value, 20, 'white' if primary else BLUE)

    def save(self, name):
        self.image.save(ASSETS / (name+'.png'), optimize=True)
        (ASSETS / (name+'.svg')).write_text('\n'.join(self.svg)+'</svg>\n', encoding='utf-8')


f = Figure('01  完整解压后，从顶层入口开始', 430)
f.rect(32, 88, 474, 392)
f.text(58, 110, '本地壁纸_R30', 23)
for y, text in [(165, '打开本地壁纸.cmd  ← 程序入口'), (218, '开始使用.html       ← 本教程'),
                (271, '教程资源 /             图像与样式'), (324, 'app /                      应用文件')]:
    f.text(58, y, text, 20)
f.rect(502, 88, 1088, 392)
f.text(528, 111, '首次使用 · 选择数据位置', 23)
f.text(528, 161, 'Windows 为当前用户推荐本地位置', 20, MUTED)
f.rect(528, 207, 908, 253, '#f2f5fa')
f.text(543, 216, '使用推荐位置，或浏览选择其他盘', 18, MUTED)
f.button(922, 207, 134, '浏览…')
f.text(528, 281, '这里将保存导入副本、列表、设置和封面', 20)
f.button(844, 328, 212, '开始使用', True)
f.save('first-use')

f = Figure('02  四种位置，各有用途', 500)
cards = [(32, 92, '软件数据位置', '保存你的库、设置和图片', '导入副本也会占用这里的空间', BLUE),
         (574, 92, '壁纸文件来源', '本机已经取得的联想缓存', '识别后检查，由你明确导入', '#227760'),
         (32, 288, '可选名称缓存', '从已有商店缓存补充名称', '缺失时继续使用原名称或资源 ID', '#7c50a8'),
         (574, 288, '播放组件目录', '你合法准备的兼容本地组件', '通过身份核验后才能预览和播放', '#9b5a21')]
for x, y, title, line1, line2, color in cards:
    f.rect(x, y, x+514, y+174)
    f.text(x+24, y+20, title, 25, color)
    f.text(x+24, y+70, line1, 21)
    f.text(x+24, y+111, line2, 19, MUTED)
f.save('locations')

f = Figure('03  先看图认资源，再明确导入', 460)
f.rect(32, 90, 1088, 424)
f.text(56, 111, '导入联想已下载壁纸', 24)
f.text(58, 165, '名称 / ID', 19, MUTED)
f.text(390, 165, '检查', 19, MUTED)
f.text(532, 165, '封面', 19, MUTED)
for y, label, color in [(212, '壁纸 A', '#dbe8ff'), (286, '壁纸 B', '#e4e1f9')]:
    f.rect(56, y, 664, y+61, '#f4f7fc')
    f.rect(68, y+8, 140, y+53, color)
    f.text(164, y+16, label, 21)
    f.text(386, y+16, '通过', 20, '#227760')
    f.text(528, y+16, '视频画面', 20, BLUE)
f.rect(706, 160, 1058, 314, '#dce7fc')
f.text(769, 202, '选中条目的识别图', 23, BLUE)
f.text(743, 245, '示意区域 · 使用你自己的资源', 17, MUTED)
f.text(58, 375, '图片帮助你辨认资源，播放效果请再预览', 20, MUTED)
f.button(856, 350, 202, '导入所选壁纸', True)
f.save('import')

f = Figure('04  选中、预览确认、应用是三步', 470)
f.rect(32, 92, 1088, 428)
f.rect(56, 122, 330, 296, '#e5edff', BLUE)
f.text(84, 151, '壁纸 A', 24)
f.text(84, 213, '当前选中', 22, BLUE)
f.rect(352, 122, 626, 296, '#f1f5fa')
f.text(380, 151, '壁纸 B', 24)
f.text(380, 213, '正在使用', 22, '#227760')
f.text(56, 331, '选中 A 时，B 可以仍在播放。', 22)
f.text(56, 371, '点“预览”或“设为桌面壁纸”后才执行切换。', 20, MUTED)
f.button(708, 122, 348, '1  预览')
f.button(708, 192, 348, '2  确认画面')
f.button(708, 262, 348, '3  设为桌面壁纸', True)
f.text(710, 335, '在预览窗确认实际画面', 20, MUTED)
f.text(710, 374, '截图封面可另点“截取封面”', 19, MUTED)
f.save('preview')

f = Figure('05  隐藏后，从蓝色图标找回', 460)
f.rect(32, 92, 500, 424)
f.text(58, 120, '桌面播放时', 24)
f.button(58, 178, 416, '关闭界面，继续播放', True)
f.text(58, 252, '在 Windows 通知区域查找图标', 21)
f.text(58, 291, '折叠在小箭头里时，先展开面板', 20, MUTED)
f.text(58, 358, '点图标  →  回到同一主界面', 23, BLUE)
f.rect(530, 92, 1088, 424)
f.text(558, 120, '右键图标的菜单', 24)
for y, label in [(179, '打开主界面'), (228, '选择 / 更换壁纸'),
                 (277, '停止播放并恢复原桌面'), (326, '彻底退出')]:
    f.text(568, y, label, 23, BLUE if y==277 else INK)
f.text(558, 382, '菜单项目随当前运行状态启用', 18, MUTED)
f.save('tray')

with Image.open(ROOT/'assets/LocalWallpaper.ico') as icon:
    icon.convert('RGBA').resize((96,96),Image.Resampling.LANCZOS).save(ASSETS/'app-icon.png')
print('5 original schematic PNG/SVG figures and selected app icon prepared')
