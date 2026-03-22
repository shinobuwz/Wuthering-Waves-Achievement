import time

from .predict_system import TextSystem
from .utils import infer_args as init_args
from .utils import str2bool, draw_ocr
import argparse
import sys


class ONNXPaddleOcr(TextSystem):
    def __init__(self, **kwargs):
        # 默认参数
        parser = init_args()
        inference_args_dict = {}
        for action in parser._actions:
            inference_args_dict[action.dest] = action.default
        params = argparse.Namespace(**inference_args_dict)

        # params.rec_image_shape = "3, 32, 320"
        params.rec_image_shape = "3, 48, 320"

        # 根据传入的参数覆盖更新默认参数
        params.__dict__.update(**kwargs)

        # 初始化模型
        super().__init__(params)

    def ocr(self, img, det=True, rec=True, cls=False):
        """
        OCR识别，返回文本字符串列表
        :param img: 输入图像
        :param det: 是否进行文本检测
        :param rec: 是否进行文本识别
        :param cls: 是否进行角度分类
        :return: 文本字符串列表，如 ['文本1', '文本2', ...]
        """
        if det and rec:
            dt_boxes, rec_res = self.__call__(img, cls)
            # 只返回文本字符串列表
            return [res[0] for res in rec_res]
        elif det and not rec:
            dt_boxes = self.text_detector(img)
            return [box.tolist() for box in dt_boxes]
        else:
            if not isinstance(img, list):
                img = [img]
            if self.use_angle_cls and cls:
                img, cls_res_tmp = self.text_classifier(img)
                if not rec:
                    return cls_res_tmp
            rec_res = self.text_recognizer(img)
            # 只返回文本字符串列表
            return [res[0] for res in rec_res]


def sav2Img(org_img, result, name="draw_ocr.jpg"):
    # 显示结果
    from PIL import Image

    result = result[0]
    # image = Image.open(img_path).convert('RGB')
    # 图像转BGR2RGB
    image = org_img[:, :, ::-1]
    boxes = [line[0] for line in result]
    txts = [line[1][0] for line in result]
    scores = [line[1][1] for line in result]
    im_show = draw_ocr(image, boxes, txts, scores)
    im_show = Image.fromarray(im_show)
    im_show.save(name)


if __name__ == "__main__":
    import cv2

    model = ONNXPaddleOcr(use_angle_cls=True, use_gpu=False)

    img = cv2.imread(
        "/data2/liujingsong3/fiber_box/test/img/20230531230052008263304.jpg"
    )
    s = time.time()
    result = model.ocr(img)
    e = time.time()
    print("total time: {:.3f}".format(e - s))
    print("result:", result)
    for box in result[0]:
        print(box)

    sav2Img(img, result)
