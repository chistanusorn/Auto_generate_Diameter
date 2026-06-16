SELECT DISTINCT
    coatlt.lot_number AS coat_lot_number,
    coatlt.sequence_number AS coat_lot_seq,
    diplt.lot_number AS dip_lot_number,
    diplt.sequence_number AS diplt_seq,
    l.rl_type,
    h.tray_number,
    h.rlp_type AS rlp_order,
    coatlt.rlp_type AS rlp_lot,
    coatlt.rxarrangement_number,
    h.traylot_number,
    CASE
        WHEN h.order_route_type = '01' THEN 'Telephone'
        WHEN h.order_route_type = '02' THEN 'Fax'
        WHEN h.order_route_type = '03' THEN 'Hit'
        WHEN h.order_route_type = '04' THEN 'Misc'
        WHEN h.order_route_type = '05' THEN 'EDI'
        WHEN h.order_route_type = '06' THEN 'Lab(RXSIJI)'
        WHEN h.order_route_type = '07' THEN 'E-Mail'
        WHEN h.order_route_type = '08' THEN 'Application'
        WHEN h.order_route_type = '09' THEN 'Web'
        WHEN h.order_route_type = '10' THEN 'Mail'
        WHEN h.order_route_type = '11' THEN 'OtherLab'
        WHEN h.order_route_type = '12' THEN 'WF'
        WHEN h.order_route_type = '13' THEN ''
        WHEN h.order_route_type = '14' THEN 'SOP'
        WHEN h.order_route_type = '15' THEN 'Vlink'
        WHEN h.order_route_type = '16' THEN 'ORD7'
        WHEN h.order_route_type = '17' THEN 'ORD8'
        WHEN h.order_route_type = '18' THEN 'ASIA'
        WHEN h.order_route_type = '19' THEN 'INDO(HOLT)'
        WHEN h.order_route_type = '20' THEN 'AX'
        WHEN h.order_route_type = '21' THEN 'WebHOYALOG'
        WHEN h.order_route_type = '22' THEN 'LDS'
        WHEN h.order_route_type = '23' THEN 'Innovation'
        WHEN h.order_route_type = '24' THEN 'DVI'
        WHEN h.order_route_type = '25' THEN 'HUT'
        WHEN h.order_route_type = '26' THEN 'MASS-RX'
        WHEN h.order_route_type = '27' THEN 'CDN for HLPO'
        WHEN h.order_route_type = '97' THEN 'ORD8-VV SHADOW'
        WHEN h.order_route_type = '98' THEN 'ORD8-VV'
        WHEN h.order_route_type = '99' THEN 'SHADOW'
        ELSE 'Unknown'
    END AS order_route_type_name,
    CASE
        WHEN l.item_type = '001' THEN 'Blanks'
        WHEN l.item_type = '002' THEN 'Semi'
        WHEN l.item_type = '011' THEN 'Lens(Finish)'
        WHEN l.item_type = '012' THEN 'Lens(RX)'
        WHEN l.item_type = '013' THEN 'Color Type'
        WHEN l.item_type = '014' THEN 'Color'
        WHEN l.item_type = '015' THEN 'Coat'
        WHEN l.item_type = '016' THEN 'Special Instruction'
        WHEN l.item_type = '017' THEN 'Job Instruction'
        WHEN l.item_type = '018' THEN 'Service'
        WHEN l.item_type = '021' THEN 'Frame'
        WHEN l.item_type = '022' THEN 'Frame Parts'
        WHEN l.item_type = '031' THEN 'Instrulment'
        WHEN l.item_type = '032' THEN 'Instrument parts'
        WHEN l.item_type = '051' THEN 'Sales promotion'
        WHEN l.item_type = '061' THEN 'Lab material'
        WHEN l.item_type = '062' THEN 'Mold'
        WHEN l.item_type = '063' THEN 'Gasket'
        WHEN l.item_type = '064' THEN 'Tool'
        WHEN l.item_type = '071' THEN 'HIT'
        WHEN l.item_type = '081' THEN 'Coupon'
        WHEN l.item_type = '091' THEN 'Others'
        ELSE 'Unknown'
    END AS item_type_name,
    l.local_semi_diameter,
    c.wsdt13_cgkk,
    c.wsdt11_kos,
    f.zkdto_zko,
    diplt.used_flag,
    CASE
        WHEN l.item_type = '001' THEN
            CASE
                WHEN c.wsdt11_kos > c.wsdt13_cgkk THEN c.wsdt13_cgkk
                ELSE c.wsdt11_kos
            END
        WHEN l.item_type = '002' THEN
            CASE
                WHEN l.local_semi_diameter > c.wsdt13_cgkk THEN c.wsdt13_cgkk
                ELSE l.local_semi_diameter
            END
        WHEN l.item_type = '011' THEN f.zkdto_zko
        ELSE 99
    END AS diameter
FROM llab_t_coatlog coatlt
LEFT JOIN llab_t_rxarrangementheader h
    ON coatlt.rxproduction_place_code = h.rxproduction_place_code
    AND coatlt.rxarrangement_number = h.rxarrangement_number
LEFT JOIN llab_t_coatlog diplt
    ON h.rxproduction_place_code = diplt.rxproduction_place_code
    AND h.rxarrangement_number = diplt.rxarrangement_number
    AND h.order_number = diplt.order_number
    AND diplt.hard_ar_lot_number_type = '4'
    AND diplt.lot_datetime >= (
        SELECT MAX(lot_datetime)
        FROM llab_t_coatlog dlt
        WHERE dlt.rxproduction_place_code = coatlt.rxproduction_place_code
        AND dlt.rxarrangement_number = coatlt.rxarrangement_number
        AND dlt.order_number = coatlt.order_number
        AND dlt.hard_ar_lot_number_type = '4'
    )
LEFT JOIN llab_t_rxarrangementlensdetail l
    ON h.rxarrangement_number = l.rxarrangement_number
    AND h.rxproduction_place_code = l.rxproduction_place_code
LEFT JOIN llab_t_productcalc c
    ON l.rxarrangement_number = c.rxarrangement_number
    AND l.rxproduction_place_code = c.rxproduction_place_code
    AND l.rl_type = c.rl_type
LEFT JOIN llab_t_designcalc_outfinish f
    ON l.rxarrangement_number = f.rxarrangement_number
    AND l.rxproduction_place_code = f.rxproduction_place_code
    AND l.rl_type = f.rl_type
WHERE coatlt.rxproduction_place_code = %s
AND coatlt.lot_number = %s
ORDER BY coatlt.sequence_number ASC, diplt_seq ASC, rl_type DESC
