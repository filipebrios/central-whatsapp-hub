-- Defaults compartilhados; nunca sobrescrever configuração já feita pela equipe.
insert into public.agenda_service_types(company_id,id,label,color,sort_order,is_active)
select c.id,s.id,s.label,s.color,s.sort_order,s.is_active
from public.agenda_companies c
cross join (values
 ('desinsetizacao','Desinsetização','#039BE5',10,true),
 ('desratizacao','Desratização','#3F51B5',20,true),
 ('descupinizacao','Descupinização','#8E24AA',30,true),
 ('reservatorio','Higienização de reservatório','#00796B',40,true),
 ('caixa_gordura','Limpeza de caixa de gordura','#616161',50,true),
 ('sanitizacao','Sanitização','#43A047',60,true),
 ('capina_quimica','Capina química','#6F7D2F',70,true),
 ('orcamento','Orçamento','#F6BF26',80,true),
 ('confirmacao','Confirmação / vistoria','#0B8043',90,true),
 ('interno','Internos / curso','#F4511E',100,true),
 ('cancelado','Cancelado / reagendar','#D50000',110,false),
 ('outros','Não identificado','#E0E5E8',120,false)
) as s(id,label,color,sort_order,is_active)
on conflict(company_id,id) do nothing;
